using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Notes.Core.Logging;

namespace Notes.Core.Memory;

/// <summary>
/// Пишет в отдельный файл всё, что сервер прислал клиенту для исполнения: ключ, адрес, флаги, строки и тело.
/// Только читает память — ни одной записи в клиент.
/// <para>
/// Зачем отдельный файл, а не общий лог бота: записи длинные (тело кодом), редкие и нужны только для разбора —
/// в окне и в общем логе они мешают. Поэтому источника в <see cref="Logging.LogSources"/> нет, в интерфейсе
/// ничего не появляется.
/// </para>
/// <para>
/// Сравнение — по первым <see cref="FingerprintBytes"/> байтам тела. По всему блоку сравнивать нельзя: клиент
/// держит в одном буфере и код, и рабочие данные, и хвост меняется несколько раз в секунду (проверено на живом
/// клиенте 30.10 — у ключа 1106 шевелились +0x4C, +0x60, +0x6C, а первые байты кода стояли).
/// </para>
/// <para>
/// Карта живёт до конца сеанса, по одному ключу хранится одно тело — последнее (повторная присылка освобождает
/// старое, см. <c>FUN_004b8cc0</c>). Поэтому «изменение» — это не правка на месте, а новый блок под тем же ключом.
/// </para>
/// </summary>
public sealed class ServerCodeLog : IDisposable
{
    /// <summary>Как часто перечитываем карту. Блок появляется и исполняется в одном тике, так что смысл не в скорости, а в охвате.</summary>
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(2);

    /// <summary>Сколько первых байт тела считаются «отпечатком»: по ним видно подмену, и они не шевелятся.</summary>
    public const int FingerprintBytes = 64;

    /// <summary>Флаг узла «в блоке исполняемый код»: только у таких следим за подменой тела.</summary>
    public const byte ExecutableFlag = 0x04;

    /// <summary>
    /// Сколько байт тела писать в файл. Килобайта хватает с запасом: заглушка-рукопожатие — 21 байт, служебные
    /// записи с дескрипторами импорта — сотни байт. Дальше начинается соседняя память кучи: оттуда лезут чужие
    /// «строки» (проверено 30.10: при 8 КБ в строках появились <c>2+&lt;&gt;C</c>, <c>gfj=w</c>).
    /// </summary>
    public const int BodyLimit = 0x400;

    private const int PageSize = 0x1000;

    private readonly object _lock = new();
    private readonly IMemory _memory;
    private readonly uint _mapHead;
    private readonly uint _ownKeyMin;
    private readonly string _directory;
    private readonly Logging.ILogger _log;
    private readonly Dictionary<ushort, Known> _known = new();
    private Thread? _thread;
    private CancellationTokenSource? _stop;
    private bool _headerWritten;
    private bool _failed;
    private bool _disposed;

    /// <param name="directory">Куда писать файлы <c>server-code-ГГГГ-ММ-ДД.log</c>.</param>
    /// <param name="log">Куда сказать, если карта не читается (в окне этого не видно).</param>
    public ServerCodeLog(IMemory memory, uint mapHead, uint ownKeyMin, string directory, Logging.ILogger log)
    {
        _memory = memory;
        _mapHead = mapHead;
        _ownKeyMin = ownKeyMin;
        _directory = directory;
        _log = log;
    }

    /// <summary>Путь к файлу за сегодняшний день — для тестов и чтобы человек знал, где смотреть.</summary>
    public string PathFor(DateTime day)
        => Path.Combine(_directory, $"server-code-{day:yyyy-MM-dd}.log");

    /// <summary>
    /// Начать следить. Ничего не делает, если в профиле не задана карта: на этом клиенте механизм не проверяли.
    /// </summary>
    /// <param name="moduleBase">
    /// Адрес, по которому загружен exe клиента. В профиле смещения — от начала модуля (у клиента ASLR),
    /// а читать надо настоящий адрес: база + смещение. Из памяти модуля вычислить нельзя — только снаружи.
    /// </param>
    public static ServerCodeLog? Start(IMemory memory, uint moduleBase, Profiles.ServerCodeOffsets? offsets,
        string directory, Logging.ILogger log)
    {
        // Карта не настроена — молчим: в остальных профилях ключ пустой намеренно, это не ошибка
        if (offsets is null || offsets.MapHead == 0)
            return null;

        var watcher = new ServerCodeLog(memory, moduleBase + offsets.MapHead, offsets.OwnKeyMin, directory, log);
        watcher.Start();
        return watcher;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_thread is not null)
                return;

            var stop = _stop = new CancellationTokenSource();
            _thread = new Thread(() => Loop(stop.Token)) { IsBackground = true, Name = "Notes: серверный код" };
            _thread.Start();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        Thread? thread;
        lock (_lock)
        {
            thread = _thread;
            _stop?.Cancel();
            _thread = null;
        }

        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));
        _stop?.Dispose();
    }

    private void Loop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                Scan(DateTime.Now);
                _failed = false;
            }
            catch (MemoryAccessException e)
            {
                // Один раз говорим, дальше молчим: кривой mapHead — не повод засорять лог каждые две секунды
                if (!_failed)
                    _log.Warning($"Карта серверного кода не читается ({e.Message}) — смещение в профиле не от этого клиента");
                _failed = true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (!_failed)
                    _log.Warning("Файл серверного кода не пишется: " + e.Message);
                _failed = true;
            }

            stop.WaitHandle.WaitOne(Period);
        }
    }

    /// <summary>Один проход: обойти карту, сравнить с прошлым разом, записать новое. Отдельно — чтобы тесты звали без потока.</summary>
    public void Scan(DateTime now)
    {
        // codeBytes: 0 — тела здесь не нужны: сначала только адреса, а тело читаем у тех, кто новый или изменился
        var entries = ServerCodeMap.Read(_memory, _mapHead, _ownKeyMin, codeBytes: 0);
        var seen = new HashSet<ushort>();
        var lines = new List<string>();

        foreach (var entry in entries)
        {
            if (entry.Own || entry.Code == 0)
                continue;

            var key = (ushort)entry.Key;
            seen.Add(key);
            // Флаг 0x04 — «в блоке исполняемый код» (0x07 у заглушек; у служебных записей 0x03/0x0B его нет).
            // Только у таких блоков имеет смысл следить за подменой тела: у служебных в тех же байтах живут
            // рабочие данные, и они шевелятся постоянно (проверено 30.10 — 20 записей «изменился» за 6 с).
            var executable = (entry.Flags & ExecutableFlag) != 0;
            var head = executable ? ReadHead(entry.Code) : [];
            var known = _known.TryGetValue(key, out var previous);
            if (known && previous!.Address == entry.Code && (!executable || Same(previous.Head, head)))
            {
                // Блок тот же и не изменился. Флаги могли поменяться (0x03 → 0x07, когда по записи пришёл
                // настоящий код) — это стоит увидеть, но тело уже записано, второй раз его не пишем
                if (previous.Flags != entry.Flags)
                {
                    lines.Add(Row(now, "флаги", key, entry.Code, entry.Flags, [], []));
                    _known[key] = previous with { Flags = entry.Flags };
                }

                continue;
            }

            // Новый ключ, новый адрес или другое тело — только тогда читаем блок целиком
            var body = ReadBody(entry.Code);
            var strings = ServerCodeMap.Strings(body, entry.Code);
            var what = !known ? "новый" : previous!.Address != entry.Code ? "переехал" : "изменился";

            _known[key] = new Known(entry.Code, head, entry.Flags);
            lines.Add(Row(now, what, key, entry.Code, entry.Flags, body, strings));
        }

        foreach (var key in _known.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            lines.Add(Row(now, "пропал", key, _known[key].Address, 0, [], []));
            _known.Remove(key);
        }

        // Заголовок пишем и тогда, когда событий нет: иначе по пустой папке не понять, работает лог или нет
        if (lines.Count > 0 || !_headerWritten)
            Write(now, entries, lines);
    }

    private void Write(DateTime now, IReadOnlyList<ServerCodeEntry> entries, IReadOnlyList<string> lines)
    {
        Directory.CreateDirectory(_directory);
        var path = PathFor(now);

        // Файл открывается на дозапись и сразу закрывается — его можно читать и удалять, пока бот работает
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        if (!_headerWritten)
        {
            var server = entries.Count(e => !e.Own);
            writer.WriteLine($"# Сессия {now:yyyy-MM-dd HH:mm:ss}: записей {entries.Count}, с сервера {server}, "
                             + $"свои {entries.Count - server} (свои не пишем)");
            writer.WriteLine("# время\tсобытие\tключ\tадрес\tфлаги\tбайт\tстроки\tтело");
            _headerWritten = true;
        }

        foreach (var line in lines)
            writer.WriteLine(line);
    }

    private static string Row(DateTime now, string what, ushort key, uint code, byte flags, byte[] body,
        IReadOnlyList<string> strings)
        => string.Join("\t",
            now.ToString("HH:mm:ss"),
            what,
            key.ToString(),
            $"0x{code:X8}",
            $"0x{flags:X2}",
            body.Length.ToString(),
            strings.Count == 0 ? "-" : string.Join(" | ", strings),
            body.Length == 0 ? "-" : BitConverter.ToString(body).Replace('-', ' '));

    /// <summary>Первые байты тела — для сравнения. Не читается (страницу освободили) — пусто.</summary>
    private byte[] ReadHead(uint code)
    {
        var head = new byte[FingerprintBytes];
        return _memory.TryRead(code, head, head.Length) ? head : [];
    }

    /// <summary>
    /// Тело блока постранично: блок бывает короче страницы, а за её концом уже чужая память.
    /// Читаем до <see cref="BodyLimit"/> или до первой нечитаемой страницы.
    /// </summary>
    private byte[] ReadBody(uint code)
    {
        var result = new List<byte>(0x400);
        var page = new byte[PageSize];
        for (var done = 0; done < BodyLimit;)
        {
            var address = code + (uint)done;
            var untilPageEnd = (int)(PageSize - address % PageSize);
            var want = Math.Min(untilPageEnd, BodyLimit - done);
            if (want <= 0 || !_memory.TryRead(address, page, want))
                break;

            result.AddRange(page.Take(want));
            done += want;
        }

        return result.ToArray();
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
            return false;
        for (var i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i])
                return false;
        }

        return true;
    }

    private sealed record Known(uint Address, byte[] Head, byte Flags);
}

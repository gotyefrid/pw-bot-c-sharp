using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Notes.Core.Logging;
using Notes.Core.Settings;

namespace Notes.Core.Stats;

/// <summary>
/// Статистика одного персонажа: события в файлах <c>stats\&lt;сервер&gt;\Ник\ГГГГ-ММ.jsonl</c> рядом с exe (строка — событие,
/// месяц — по местному времени) и итоги за период (<see cref="Summary"/>). Пишет мозг из потока снимков, читает окно — под
/// замком. В памяти — с начала прошлого месяца: хватает на неделю, начатую в прошлом месяце.
/// <para>
/// Старые файлы читает <see cref="Load"/> — можно в фоне, пока бот уже пишет: читается только то, что было в файлах при
/// создании книги, новое уже в памяти, поэтому ничего не двоится.
/// </para>
/// </summary>
public sealed class StatsBook : IStatsSink
{
    private static readonly JsonSerializerSettings Json = new()
    {
        Converters = { new StringEnumConverter(new CamelCaseNamingStrategy()) },
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
        Formatting = Formatting.None,
    };

    private readonly object _lock = new();
    private readonly ILogger _log;
    // Файлы прошлого и этого месяца и сколько в них было байт при создании книги — столько и читает Load
    private readonly IReadOnlyList<(string Path, long Length)> _old;
    private List<StatEvent> _events = [];
    private bool _live;
    private bool _writeFailed;

    /// <param name="directory">Папка персонажа (<see cref="DirectoryFor"/>).</param>
    /// <param name="nowLocal">Сейчас, местное время: какие месяцы читать.</param>
    public StatsBook(string directory, DateTime nowLocal, ILogger? log = null)
    {
        Directory = directory;
        _log = log ?? NullLogger.Instance;
        var month = new DateTime(nowLocal.Year, nowLocal.Month, 1);
        _old = new[] { month.AddMonths(-1), month }
            .Select(PathFor)
            .Select(path => (path, File.Exists(path) ? new FileInfo(path).Length : 0))
            .Where(f => f.Item2 > 0)
            .ToList();
    }

    public string Directory { get; }

    /// <summary>Папка персонажа: <c>stats\сервер\Ник</c> в папке бота.</summary>
    public static string DirectoryFor(string root, string server, string nick)
        => Path.Combine(root, "stats", CharacterSettings.SafeName(server, nameof(server)), CharacterSettings.SafeName(nick, nameof(nick)));

    /// <summary>Пришло событие или дочитаны старые — итоги стали другими. Из потока снимков или фона.</summary>
    public event Action? Changed;

    /// <summary>Идёт сессия, начатая в этой книге (бот запущен).</summary>
    public bool Live
    {
        get
        {
            lock (_lock)
                return _live;
        }
    }

    /// <summary>Прочитать старые события из файлов. Испорченные строки пропускаются.</summary>
    public void Load()
    {
        var loaded = new List<StatEvent>();
        foreach (var (path, length) in _old)
        {
            try
            {
                loaded.AddRange(Read(path, length));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log.Warning($"Статистика: не прочитать {Path.GetFileName(path)} — {e.Message}");
            }
        }

        lock (_lock)
            _events = loaded.Concat(_events).OrderBy(e => e.Time).ToList();
        Changed?.Invoke();
    }

    public void Add(StatEvent stat)
    {
        lock (_lock)
        {
            // Время снимков идёт вперёд; на всякий случай (перевели часы) — на своё место
            if (_events.Count > 0 && stat.Time < _events[_events.Count - 1].Time)
                _events.Insert(UpperBound(stat.Time), stat);
            else
                _events.Add(stat);

            if (stat.Kind == StatKind.SessionStart)
                _live = true;
            else if (stat.Kind == StatKind.SessionEnd)
                _live = false;
            Write(stat);
        }

        Changed?.Invoke();
    }

    /// <summary>Итоги за период; <paramref name="nowUtc"/> — сейчас (время идущей сессии и начало дня, недели, месяца).</summary>
    public StatsSummary Summary(StatsPeriod period, DateTime nowUtc)
    {
        lock (_lock)
        {
            var live = _live ? nowUtc : (DateTime?)null;
            if (StatsSummary.PeriodStart(period, nowUtc.ToLocalTime()) is { } start)
                return StatsSummary.Of(_events, start.ToUniversalTime(), DateTime.MaxValue, live);

            // Сессия — последняя начатая; не было ни одной — пусто
            var session = _events.FindLastIndex(e => e.Kind == StatKind.SessionStart);
            return session < 0 ? StatsSummary.Empty : StatsSummary.Of(_events, _events[session].Time, DateTime.MaxValue, live);
        }
    }

    private string PathFor(DateTime monthLocal) => Path.Combine(Directory, $"{monthLocal:yyyy-MM}.jsonl");

    private int UpperBound(DateTime time)
    {
        int lo = 0, hi = _events.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (_events[mid].Time <= time)
                lo = mid + 1;
            else
                hi = mid;
        }

        return lo;
    }

    private void Write(StatEvent stat)
    {
        var line = JsonConvert.SerializeObject(new Line { T = stat.Time, K = stat.Kind, N = stat.Name, A = stat.Amount }, Json) + "\n";
        var path = PathFor(stat.Time.ToLocalTime());
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var bytes = new UTF8Encoding(false).GetBytes(line);
            file.Write(bytes, 0, bytes.Length);
            _writeFailed = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Бот работает дальше; в окне итоги верные (событие в памяти), в файл не попало
            if (!_writeFailed)
                _log.Warning($"Статистика: не записать в {Path.GetFileName(path)} — {e.Message}");
            _writeFailed = true;
        }
    }

    private static IEnumerable<StatEvent> Read(string path, long length)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytes = new byte[length];
        var read = 0;
        while (read < length && file.Read(bytes, read, (int)(length - read)) is var n and > 0)
            read += n;

        var result = new List<StatEvent>();
        foreach (var text in Encoding.UTF8.GetString(bytes, 0, read).Split('\n'))
        {
            if (text.Trim().Length == 0)
                continue;
            try
            {
                if (JsonConvert.DeserializeObject<Line>(text, Json) is { } line && Enum.IsDefined(typeof(StatKind), line.K))
                    result.Add(new StatEvent(line.T, line.K, line.N ?? "", line.A));
            }
            catch (JsonException)
            {
                // Строка оборвана (выключили компьютер посреди записи) или вид из новой версии — пропускаем
            }
        }

        return result;
    }

    // Строка файла: коротко, пустое имя и «1 штука» не пишутся
    private sealed class Line
    {
        [JsonProperty("t")]
        public DateTime T { get; set; }

        [JsonProperty("k")]
        public StatKind K { get; set; }

        [JsonProperty("n", DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue("")]
        public string? N { get; set; } = "";

        [JsonProperty("a", DefaultValueHandling = DefaultValueHandling.Ignore)]
        [DefaultValue(1L)]
        public long A { get; set; } = 1;
    }
}

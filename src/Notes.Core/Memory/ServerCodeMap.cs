using System;
using System.Collections.Generic;
using System.Text;

namespace Notes.Core.Memory;

/// <summary>Одна запись карты серверного кода: ключ, адрес кода, флаги и что в нём видно текстом.</summary>
/// <param name="Key">Ключ из пакета: по нему сервер потом просит этот код вызвать.</param>
/// <param name="Node">Адрес узла карты (для отладки: переехал — видно, откуда читали).</param>
/// <param name="Code">Адрес исполняемого буфера.</param>
/// <param name="Flags">Флаги узла: 0x02 — «есть код», 0x04/0x08 — служебные ветки клиента.</param>
/// <param name="Strings">Печать внутри кода: имена библиотек, функций, окон — то, по чему видно, чем код занят.</param>
/// <param name="CodeBytes">Сколько байт кода удалось прочитать (0 — страница уже освобождена).</param>
/// <param name="Hex">Начало кода байтами — чтобы в логе остался сам код, даже когда строк нет.</param>
public sealed record ServerCodeEntry(uint Key, uint Node, uint Code, byte Flags, IReadOnlyList<string> Strings, int CodeBytes,
    string Hex = "")
{
    /// <summary>Своя команда клиента (ключ 0xFFEA и выше): зарегистрирована при старте, не с сервера.</summary>
    public bool Own { get; init; }
}

/// <summary>
/// Чтение карты пришедшего с сервера кода из живого клиента. Только чтение: ни одной записи в игру.
/// <para>
/// Устройство карты (std::map у MSVC, узел 0x18 байт): +0x00 левый, +0x04 родитель, +0x08 правый, +0x0D признак
/// «пустой», +0x0E ключ (word), +0x10 значение (указатель на код), +0x14 флаги. Корень — [голова + 4]: голова это
/// обычный узел, у которого left/right — минимум и максимум, а parent — корень. Обход от минимума нельзя: он видит
/// только свою ветку.
/// </para>
/// </summary>
public static class ServerCodeMap
{
    /// <summary>Смещения узла — одинаковые во всех сборках MSVC, но записаны константами, чтобы не путать с «найдено на живом клиенте».</summary>
    private const int NodeSize = 0x18;
    private const int OffLeft = 0x00;
    private const int OffParent = 0x04;
    private const int OffRight = 0x08;
    private const int OffIsNil = 0x0D;
    private const int OffKey = 0x0E;
    private const int OffValue = 0x10;
    private const int OffFlags = 0x14;

    /// <summary>Сколько байт кода читаем по умолчанию, если в профиле не задано: хватает на строки импорта и пролог.</summary>
    public const int DefaultCodeBytes = 0x400;

    /// <summary>Столько символов в строке и столько строк на запись — чтобы лог не распухал от одного блока.</summary>
    private const int MaxStrings = 12;
    private const int MinStringLength = 5;

    /// <summary>Сколько начальных байт кода писать в лог строкой hex: этого хватает, чтобы узнать пролог и вызовы.</summary>
    public const int HexBytes = 64;

    /// <summary>Начало буфера строкой hex: «55 8B EC …». Пусто — нечего писать.</summary>
    public static string Hex(byte[] code, int count)
        => count <= 0 ? "" : BitConverter.ToString(code, 0, count).Replace('-', ' ');

    /// <summary>
    /// Все записи карты по порядку ключей. Пустая карта (узлов нет) — пустой список; саму голову прочитать не вышло —
    /// <see cref="MemoryAccessException"/> (это значит, что смещение в профиле не отсюда).
    /// </summary>
    public static IReadOnlyList<ServerCodeEntry> Read(IMemory memory, uint mapHead, uint ownKeyMin, int codeBytes = DefaultCodeBytes)
    {
        if (mapHead == 0)
            throw new InvalidOperationException("В профиле не задана карта серверного кода (serverCode.mapHead)");

        var head = memory.ReadUInt32(mapHead);
        if (head == 0)
            throw new MemoryAccessException(mapHead, "Голова карты серверного кода не прочитана");

        var headNode = memory.ReadBytes(head, NodeSize);
        var root = BitConverter.ToUInt32(headNode, OffParent);

        var entries = new List<ServerCodeEntry>();
        var seen = new HashSet<uint> { head };
        var stack = new Stack<uint>();
        stack.Push(root);
        // Дерево на несколько тысяч узлов: больше — уже не карта, а мусор в памяти (например, после смены билда)
        var guard = 0;
        while (stack.Count > 0 && guard++ < 20_000)
        {
            var node = stack.Pop();
            if (node == 0 || !seen.Add(node))
                continue;

            var raw = new byte[NodeSize];
            if (!memory.TryRead(node, raw, raw.Length))
                continue;

            if (raw[OffIsNil] != 0)
                continue;

            var key = BitConverter.ToUInt16(raw, OffKey);
            var code = BitConverter.ToUInt32(raw, OffValue);
            var flags = raw[OffFlags];
            var strings = Array.Empty<string>();
            var read = 0;
            var hex = "";
            if (code != 0 && codeBytes > 0 && ReadUpTo(memory, code, codeBytes) is { Length: > 0 } body)
            {
                read = body.Length;
                strings = Strings(body, code);
                hex = Hex(body, Math.Min(body.Length, HexBytes));
            }

            entries.Add(new ServerCodeEntry(key, node, code, flags, strings, read, hex) { Own = key >= ownKeyMin });

            stack.Push(BitConverter.ToUInt32(raw, OffLeft));
            stack.Push(BitConverter.ToUInt32(raw, OffRight));
        }

        entries.Sort((a, b) => a.Key.CompareTo(b.Key));
        return entries;
    }

    /// <summary>
    /// Печатаемые строки, на которые указывает сам код. Клиент кладёт в один буфер и исполняемый код, и имена импортов
    /// («user32.dll», «GetWindowTextA»), а байты соседних указателей портят сплошной проход. Поэтому идём по указателям
    /// внутри буфера (каждые 4 байта) и берём только те, что ведут на строку из печатных ASCII.
    /// </summary>
    public static string[] Strings(byte[] code, uint codeStart)
    {
        var found = new List<string>(MaxStrings);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 4 <= code.Length && found.Count < MaxStrings; i += 4)
        {
            var offset = (long)BitConverter.ToUInt32(code, i) - codeStart;
            if (offset < 0 || offset >= code.Length)
                continue;

            var text = new StringBuilder();
            for (var j = (int)offset; j < code.Length && code[j] >= 0x20 && code[j] < 0x7F; j++)
                text.Append((char)code[j]);

            // Длиннее 64 — скорее совпадение, чем строка; короче 5 — обрывок данных
            if (text.Length is < MinStringLength or > 64 || !seen.Add(text.ToString()))
                continue;
            found.Add(text.ToString());
        }

        return found.ToArray();
    }

    /// <summary>
    /// Читает до <paramref name="count"/> байт: буфер кода на границе страницы бывает короче (остаток страницы ещё
    /// наш, дальше — чужая память). Ничего не прочитали — пустой массив.
    /// </summary>
    private static byte[] ReadUpTo(IMemory memory, uint address, int count)
    {
        var buffer = new byte[count];
        if (memory.TryRead(address, buffer, count))
            return buffer;

        const uint pageSize = 0x1000;
        var untilPageEnd = (int)(pageSize - address % pageSize);
        if (untilPageEnd < count && memory.TryRead(address, buffer, untilPageEnd))
        {
            Array.Resize(ref buffer, untilPageEnd);
            return buffer;
        }

        // Совсем не читается — страницу уже освободили: записи осталась, кода нет
        return Array.Empty<byte>();
    }
}

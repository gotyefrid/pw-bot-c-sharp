using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BotCH.Core.GameFiles;

/// <summary>Картинка иконки: пиксели BGRA (по 4 байта, строки сверху вниз), прозрачное — альфа 0.</summary>
public sealed class IconImage(int width, int height, byte[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Pixels { get; } = pixels;
}

/// <summary>
/// Набор иконок игры: element\surfaces.pck → surfaces\iconset\iconlist_&lt;набор&gt;.txt и .dds.
/// <para>
/// В .txt (GBK, по строке) — ширина и высота иконки, число строк и столбцов сетки, дальше имена файлов иконок: n-е имя —
/// n-я клетка сетки слева направо, сверху вниз. .dds — вся сетка одной картинкой, сжатой DXT1 (блоки 4×4 по 8 байт: два цвета
/// 565 и по 2 бита на пиксель). Иконку распаковываем, только когда её просят, и запоминаем.
/// </para>
/// </summary>
public sealed class IconAtlas
{
    private static readonly string[] Archives = ["surfaces.pck", "surfaces2.pck"];
    private static readonly Encoding Gbk = Encoding.GetEncoding(936);
    private const int DdsHeaderSize = 128;
    private const uint DdsMagic = 0x20534444; // «DDS »
    private const uint Dxt1 = 0x31545844; // «DXT1»

    private readonly Dictionary<string, int> _cells;
    private readonly int _listed;
    private readonly Dictionary<int, IconImage> _decoded = new();
    private readonly byte[] _dds;
    private readonly int _width;
    private readonly int _columns;

    private IconAtlas(Dictionary<string, int> cells, int listed, byte[] dds, int width, int iconWidth, int iconHeight, int columns)
    {
        _cells = cells;
        _listed = listed;
        _dds = dds;
        _width = width;
        IconWidth = iconWidth;
        IconHeight = iconHeight;
        _columns = columns;
    }

    public static IconAtlas Empty { get; } = new(new Dictionary<string, int>(), 0, [], 0, 0, 0, 0);

    public int IconWidth { get; }
    public int IconHeight { get; }

    /// <summary>Сколько иконок в наборе.</summary>
    public int Count => _cells.Count;

    public bool Contains(string file) => _cells.ContainsKey(file);

    /// <summary>Иконка по имени файла («治疗宠物.dds»); null — такой в наборе нет.</summary>
    public IconImage? Get(string file) => _cells.TryGetValue(file, out var cell) ? GetAt(cell) : null;

    /// <summary>Иконка по номеру клетки (с 0, как имена в списке); null — такой клетки нет.</summary>
    public IconImage? GetAt(int cell)
    {
        if (cell < 0 || cell >= _listed)
            return null;

        lock (_decoded)
        {
            if (!_decoded.TryGetValue(cell, out var icon))
                _decoded[cell] = icon = Decode(cell);
            return icon;
        }
    }

    /// <summary>
    /// Читает набор <paramref name="list"/> («skill» → iconlist_skill) из папки клиента: surfaces.pck, если там нет — surfaces2.pck.
    /// Не получилось — <see cref="Empty"/> и причина в <paramref name="problem"/>.
    /// </summary>
    /// <param name="format">Ключи .pck этого клиента — из профиля сервера.</param>
    public static IconAtlas LoadFromGameDirectory(string gameDirectory, string list, PckFormat format, out string? problem)
    {
        problem = "Не найдены surfaces.pck/surfaces2.pck в " + gameDirectory;
        foreach (var archive in Archives)
        {
            var path = Path.Combine(gameDirectory, archive);
            if (!File.Exists(path))
                continue;

            try
            {
                var files = PckArchive.ReadFiles(path, format, $"\\iconlist_{list}.txt", $"\\iconlist_{list}.dds");
                if (files[0] is not { } names || files[1] is not { } dds)
                {
                    problem = $"В {archive} нет iconlist_{list}";
                    continue;
                }

                problem = null;
                return Parse(names, dds);
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                problem = $"{archive}: {e.Message}";
            }
        }

        return Empty;
    }

    internal static IconAtlas Parse(byte[] names, byte[] dds)
    {
        var lines = Gbk.GetString(names).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 4 || !int.TryParse(lines[0], out var iconWidth) || !int.TryParse(lines[1], out var iconHeight)
            || iconWidth <= 0 || iconHeight <= 0 || iconWidth % 4 != 0 || iconHeight % 4 != 0)
            throw new InvalidDataException("Список иконок: в начале нет размера иконки");

        if (dds.Length < DdsHeaderSize || BitConverter.ToUInt32(dds, 0) != DdsMagic || BitConverter.ToUInt32(dds, 84) != Dxt1)
            throw new InvalidDataException("Картинка иконок не DXT1");

        var height = BitConverter.ToInt32(dds, 12);
        var width = BitConverter.ToInt32(dds, 16);
        var columns = width / iconWidth;
        var capacity = columns * (height / iconHeight);
        if (columns == 0 || dds.Length < DdsHeaderSize + width * height / 2)
            throw new InvalidDataException("Картинка иконок обрывается");

        // Строки и столбцы в файле не берём: сетку задаёт сама картинка
        var cells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 4; i < lines.Length && i - 4 < capacity; i++)
        {
            var name = lines[i].Trim();
            if (name.Length > 0 && !cells.ContainsKey(name))
                cells[name] = i - 4;
        }

        return new IconAtlas(cells, Math.Min(lines.Length - 4, capacity), dds, width, iconWidth, iconHeight, columns);
    }

    private IconImage Decode(int cell)
    {
        var left = cell % _columns * IconWidth;
        var top = cell / _columns * IconHeight;
        var pixels = new byte[IconWidth * IconHeight * 4];
        var colors = new byte[16];
        for (var y = 0; y < IconHeight; y += 4)
        {
            for (var x = 0; x < IconWidth; x += 4)
            {
                var block = DdsHeaderSize + (((top + y) / 4) * (_width / 4) + (left + x) / 4) * 8;
                var c0 = BitConverter.ToUInt16(_dds, block);
                var c1 = BitConverter.ToUInt16(_dds, block + 2);
                var bits = BitConverter.ToUInt32(_dds, block + 4);
                Palette(c0, c1, colors);
                for (var i = 0; i < 16; i++)
                {
                    var color = (int)(bits >> (2 * i) & 3) * 4;
                    Buffer.BlockCopy(colors, color, pixels, ((y + i / 4) * IconWidth + x + i % 4) * 4, 4);
                }
            }
        }

        return new IconImage(IconWidth, IconHeight, pixels);
    }

    // Четыре цвета блока BGRA: два свои, два между ними; если первый не больше второго — один между и прозрачный
    private static void Palette(ushort c0, ushort c1, byte[] colors)
    {
        Color565(c0, colors, 0);
        Color565(c1, colors, 4);
        for (var k = 0; k < 3; k++)
        {
            if (c0 > c1)
            {
                colors[8 + k] = (byte)((2 * colors[k] + colors[4 + k]) / 3);
                colors[12 + k] = (byte)((colors[k] + 2 * colors[4 + k]) / 3);
            }
            else
            {
                colors[8 + k] = (byte)((colors[k] + colors[4 + k]) / 2);
                colors[12 + k] = 0;
            }
        }

        colors[11] = 255;
        colors[15] = c0 > c1 ? (byte)255 : (byte)0;
    }

    private static void Color565(ushort c, byte[] colors, int at)
    {
        colors[at] = (byte)((c & 0x1F) * 255 / 31);
        colors[at + 1] = (byte)((c >> 5 & 0x3F) * 255 / 63);
        colors[at + 2] = (byte)((c >> 11) * 255 / 31);
        colors[at + 3] = 255;
    }
}

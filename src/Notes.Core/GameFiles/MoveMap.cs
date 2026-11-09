using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Notes.Core.GameFiles;

/// <summary>
/// Карта проходимости из файлов клиента: element\maps\&lt;карта&gt;\movemap\*.prmap — по ней клиент строит свой автопуть
/// (CECIntelligentRoute). Сетка клеток (обычно 1×1 м), у клетки бит «можно стоять». Карта есть не везде: у мира Comeback 1.4.6 —
/// примерно на четверти квадратов, где её нет — <see cref="Walkable"/> отвечает null («не знаем»).
/// <para>
/// Файл .prmap (AutoMove::CBitImage, загрузка в PW Classic client.exe 0xB95D00): int версия (1), int размер блока, блок — stride
/// (байт в строке), строк, ширина, высота, float размер клетки; дальше битмап, строка 0 — юг (меньшая координата Y мира), бит
/// клетки x — (байт[x &gt;&gt; 3] &gt;&gt; (x &amp; 7)) &amp; 1. Имя «rA_B-cC_D-l0»: квадраты карты по 1024 м, строки A…B считаются с севера,
/// столбцы C…D с запада (разбор имени — CECIntelligentRoute::ChangeWorldInstance, 0x801AE0). Сколько квадратов у карты — из
/// заголовка рельефа &lt;карта&gt;.trn2: +8 и +0xC — ширина и высота карты в метрах (float); центр карты — точка (0, 0) мира.
/// </para>
/// </summary>
public sealed class MoveMap
{
    /// <summary>Сторона квадрата карты, м.</summary>
    public const int TileSize = 1024;

    private static readonly Regex RegionName = new(@"^r(\d+)_(\d+)-c(\d+)_(\d+)-l0\.prmap$", RegexOptions.IgnoreCase);

    private readonly IReadOnlyList<Region> _regions;

    public MoveMap(string name, int rows, int columns, IReadOnlyList<Region> regions)
    {
        Name = name;
        Rows = rows;
        Columns = columns;
        _regions = regions;
    }

    /// <summary>Название карты, как папка в element\maps (world, a02…).</summary>
    public string Name { get; }

    /// <summary>Квадратов по 1024 м с севера на юг.</summary>
    public int Rows { get; }

    /// <summary>Квадратов по 1024 м с запада на восток.</summary>
    public int Columns { get; }

    public IReadOnlyList<Region> Regions => _regions;

    /// <summary>Можно ли стоять в точке мира (x, y): true/false, null — карты здесь нет.</summary>
    public bool? Walkable(float x, float y)
    {
        bool? answer = null;
        foreach (var region in _regions)
        {
            // Куски могут перекрываться (у мира — разные острова одних квадратов): проходимо, если проходимо хоть в одном
            if (region.Walkable(x, y) is { } here)
            {
                if (here)
                    return true;
                answer = false;
            }
        }

        return answer;
    }

    /// <summary>Карта из папки игры (element). Нет карты проходимости — null и причина в <paramref name="problem"/>.</summary>
    public static MoveMap? LoadFromGameDirectory(string gameDirectory, string map, out string? problem)
    {
        var folder = Path.Combine(gameDirectory, "maps", map);
        var terrain = Path.Combine(folder, map + ".trn2");
        if (!File.Exists(terrain))
        {
            problem = $"Нет файла рельефа {terrain}";
            return null;
        }

        var (width, height) = ReadTerrainSize(ReadShared(terrain));
        var rows = (int)Math.Round(height / TileSize);
        var columns = (int)Math.Round(width / TileSize);

        var regions = new List<Region>();
        var moveFolder = Path.Combine(folder, "movemap");
        if (Directory.Exists(moveFolder))
        {
            foreach (var file in Directory.GetFiles(moveFolder, "*.prmap"))
            {
                var m = RegionName.Match(Path.GetFileName(file));
                if (!m.Success)
                    continue;

                int N(int i) => int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
                regions.Add(Region.Read(ReadShared(file), rows, columns, N(1), N(2), N(3)));
            }
        }

        problem = regions.Count == 0 ? $"У карты {map} нет карты проходимости ({moveFolder})" : null;
        return new MoveMap(map, rows, columns, regions);
    }

    // Файлы держит клиент — читаем, не мешая ему
    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var data = new byte[stream.Length];
        var read = 0;
        while (read < data.Length && stream.Read(data, read, data.Length - read) is var n and > 0)
            read += n;
        return data;
    }

    /// <summary>Ширина и высота карты в метрах из заголовка .trn2 («2NRT», версия, float ширина, float высота).</summary>
    internal static (float Width, float Height) ReadTerrainSize(byte[] header)
    {
        if (header.Length < 16 || header[0] != (byte)'2' || header[1] != (byte)'N' || header[2] != (byte)'R' || header[3] != (byte)'T')
            throw new InvalidDataException("Не файл рельефа .trn2 (нет «2NRT» в начале)");
        return (BitConverter.ToSingle(header, 8), BitConverter.ToSingle(header, 12));
    }

    /// <summary>Один файл .prmap: прямоугольник квадратов карты и его битмап.</summary>
    public sealed class Region
    {
        private readonly byte[] _bits;
        private readonly int _stride;

        public Region(float originX, float originY, int width, int height, float cell, int stride, byte[] bits)
        {
            OriginX = originX;
            OriginY = originY;
            Width = width;
            Height = height;
            Cell = cell;
            _stride = stride;
            _bits = bits;
        }

        /// <summary>Юго-западный угол в координатах мира.</summary>
        public float OriginX { get; }

        public float OriginY { get; }

        /// <summary>Клеток с запада на восток и с юга на север.</summary>
        public int Width { get; }

        public int Height { get; }

        /// <summary>Сторона клетки, м.</summary>
        public float Cell { get; }

        /// <summary>Проходима ли клетка (номера от юго-западного угла); вне куска — null.</summary>
        public bool? IsOpen(int column, int row)
            => column < 0 || row < 0 || column >= Width || row >= Height
                ? null
                : ((_bits[row * _stride + (column >> 3)] >> (column & 7)) & 1) != 0;

        public bool? Walkable(float x, float y)
            => IsOpen((int)Math.Floor((x - OriginX) / Cell), (int)Math.Floor((y - OriginY) / Cell));

        /// <summary>
        /// Разбор файла: <paramref name="firstRow"/>, <paramref name="lastRow"/>, <paramref name="firstColumn"/> — из имени
        /// «rA_B-cC_D»; <paramref name="mapRows"/> и <paramref name="mapColumns"/> — размер карты в квадратах.
        /// </summary>
        internal static Region Read(byte[] data, int mapRows, int mapColumns, int firstRow, int lastRow, int firstColumn)
        {
            if (data.Length < 28 || BitConverter.ToInt32(data, 0) != 1)
                throw new InvalidDataException("Не карта проходимости .prmap (версия не 1)");

            var stride = BitConverter.ToInt32(data, 8);
            var height = BitConverter.ToInt32(data, 12);
            var width = BitConverter.ToInt32(data, 16);
            var cell = BitConverter.ToSingle(data, 24);
            if (stride * 8 < width || data.Length < 28 + stride * height || cell <= 0)
                throw new InvalidDataException("Карта проходимости .prmap обрезана или не того формата");

            var bits = new byte[stride * height];
            Buffer.BlockCopy(data, 28, bits, 0, bits.Length);
            var originX = firstColumn * TileSize - mapColumns * TileSize / 2f;
            var originY = (mapRows - lastRow - 1) * TileSize - mapRows * TileSize / 2f;
            return new Region(originX, originY, width, height, cell, stride, bits);
        }
    }
}

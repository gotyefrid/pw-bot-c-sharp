using System;
using System.IO;
using Notes.Core.GameFiles;
using Xunit;

namespace Notes.Tests.GameFiles;

public class MoveMapTests
{
    // Тест на файлах настоящего клиента: папка element — в переменной окружения. Нет переменной — тест пропускается
    private const string ComebackVariable = "NOTES_COMEBACK146_DIR";
    private static readonly string ComebackDirectory = Environment.GetEnvironmentVariable(ComebackVariable) ?? "";

    // Файл .prmap: версия, размер блока, stride, строк, ширина, высота, размер клетки и биты
    private static byte[] Prmap(int width, int height, Func<int, int, bool> open)
    {
        var stride = (width + 7) / 8;
        var data = new byte[28 + stride * height];
        BitConverter.GetBytes(1).CopyTo(data, 0);
        BitConverter.GetBytes(20 + stride * height).CopyTo(data, 4);
        BitConverter.GetBytes(stride).CopyTo(data, 8);
        BitConverter.GetBytes(height).CopyTo(data, 12);
        BitConverter.GetBytes(width).CopyTo(data, 16);
        BitConverter.GetBytes(height).CopyTo(data, 20);
        BitConverter.GetBytes(1f).CopyTo(data, 24);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (open(x, y))
                    data[28 + y * stride + (x >> 3)] |= (byte)(1 << (x & 7));
        return data;
    }

    [Fact]
    public void ReadsBitsFromLowestBitAndRowsFromSouth()
    {
        // Карта 1×1 квадрат: начало куска — (−512; −512). Открыта одна клетка: столбец 9, строка 2
        var region = MoveMap.Region.Read(Prmap(1024, 1024, (x, y) => x == 9 && y == 2), 1, 1, 0, 0, 0);

        Assert.Equal(-512f, region.OriginX);
        Assert.Equal(-512f, region.OriginY);
        Assert.True(region.IsOpen(9, 2));
        Assert.False(region.IsOpen(8, 2));
        Assert.False(region.IsOpen(9, 3));
        Assert.True(region.Walkable(-512 + 9.5f, -512 + 2.5f));
        Assert.Null(region.IsOpen(1024, 0));
    }

    [Fact]
    public void PlacesRegionByNameInMapOfTiles()
    {
        // Мир 8×11 квадратов, кусок r1_5-c4_5: столбцы 4–5 с запада, строки 1–5 с севера → юго-западный угол (0; −512)
        var region = MoveMap.Region.Read(Prmap(2048, 5120, (_, _) => true), 11, 8, 1, 5, 4);

        Assert.Equal(0f, region.OriginX);
        Assert.Equal(-512f, region.OriginY);
    }

    [Fact]
    public void AnswersUnknownOutsideRegionsAndOpenIfAnyOverlapIsOpen()
    {
        var closed = MoveMap.Region.Read(Prmap(1024, 1024, (_, _) => false), 1, 1, 0, 0, 0);
        var open = MoveMap.Region.Read(Prmap(1024, 1024, (x, _) => x < 10), 1, 1, 0, 0, 0);
        var map = new MoveMap("a02", 1, 1, [closed, open]);

        Assert.True(map.Walkable(-510, 0));
        Assert.False(map.Walkable(0, 0));
        Assert.Null(map.Walkable(600, 0));
    }

    [Fact]
    public void ReadsMapSizeFromTerrainHeader()
    {
        // Заголовок world.trn2 Comeback 1.4.6: «2NRT», версия 1, ширина 8192, высота 11264
        var header = new byte[] { 0x32, 0x4E, 0x52, 0x54, 1, 0, 0, 0, 0, 0, 0, 0x46, 0, 0, 0x30, 0x46 };

        Assert.Equal((8192f, 11264f), MoveMap.ReadTerrainSize(header));
        Assert.Throws<InvalidDataException>(() => MoveMap.ReadTerrainSize(new byte[16]));
    }

    [Fact]
    public void RealClientWorldHasPartialMap()
    {
        Assert.SkipUnless(Directory.Exists(ComebackDirectory), $"Не задана {ComebackVariable} — папка element клиента Comeback 1.4.6");

        var map = MoveMap.LoadFromGameDirectory(ComebackDirectory, "world", out var problem);

        Assert.Null(problem);
        Assert.NotNull(map);
        Assert.Equal(11, map.Rows);
        Assert.Equal(8, map.Columns);
        Assert.True(map.Regions.Count > 5);
        // Открытое поле с камнями (проверено 09.11 на живом клиенте: тут стоял перс) и юго-восток мира, где карты у клиента нет
        Assert.True(map.Walkable(336.7f, -400.6f));
        Assert.Null(map.Walkable(1635.9f, -1905.3f));
    }
}

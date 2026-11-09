using System;
using System.IO;
using System.Linq;
using System.Text;
using Notes.Core.GameFiles;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.GameFiles;

public class SceneObstaclesTests
{
    // Тест на файлах настоящего клиента: папка element — в переменной окружения. Нет переменной — тест пропускается
    private const string ComebackVariable = "NOTES_COMEBACK146_DIR";
    private static readonly string ComebackDirectory = Environment.GetEnvironmentVariable(ComebackVariable) ?? "";

    // Модель .bmd: один меш-треугольник и один кирпич — куб от (10, 0, 20) до (12, 3, 24) (x, высота, y)
    private static byte[] Model(bool withPrefix = true)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes("MOXB"));
        if (withPrefix)
        {
            w.Write(unchecked((int)0x80000001));
            w.Write((byte)0);
        }

        w.Write(0x10000002);
        for (var i = 0; i < 12; i++)
            w.Write(1f);
        w.Write(1); // мешей
        w.Write(0x10000005);
        w.Write(new byte[64 + 256]);
        w.Write(3); // вершин
        w.Write(1); // треугольников
        w.Write(new byte[3 * 24 + 6 + 3 * 12 + 3 * 8 + 0x30]);
        w.Write(Encoding.ASCII.GetBytes("MATERIAL: \0"));
        w.Write(new byte[69]);

        w.Write(1); // кирпичей
        w.Write(2); // список из двух int
        w.Write(7);
        w.Write(8);
        foreach (var f in new[] { 11f, 1.5f, 22f, 1f, 1.5f, 2f, 10f, 0f, 20f, 12f, 3f, 24f })
            w.Write(f);
        w.Write(0);
        var planes = new[]
        {
            (-1f, 0f, 0f, -10f), (1f, 0f, 0f, 12f), (0f, -1f, 0f, 0f), (0f, 1f, 0f, 3f), (0f, 0f, -1f, -20f), (0f, 0f, 1f, 24f),
        };
        w.Write(planes.Length);
        foreach (var (x, h, y, d) in planes)
        {
            w.Write(x);
            w.Write(h);
            w.Write(y);
            w.Write(d);
            w.Write((byte)0);
        }

        return stream.ToArray();
    }

    [Fact]
    public void ReadsBrushAfterMeshes()
    {
        foreach (var data in new[] { Model(), Model(withPrefix: false) })
        {
            var brush = Assert.Single(SceneObstacles.ReadModel(data));

            Assert.Equal(10f, brush.MinX);
            Assert.Equal(24f, brush.MaxY);
            Assert.Equal(6, brush.Planes.Count);
        }
    }

    [Fact]
    public void BlocksOnlyInsideAndAtHeightOfBody()
    {
        var obstacles = new SceneObstacles(SceneObstacles.ReadModel(Model()));

        Assert.True(obstacles.Blocks(11, 22, 1, 2));
        Assert.True(obstacles.Blocks(11, 22, -2, 0.5f));
        Assert.False(obstacles.Blocks(13, 22, 0, 3));
        Assert.False(obstacles.Blocks(11, 22, 3.5f, 5));
    }

    [Fact]
    public void SlopedPlaneLimitsHeight()
    {
        // Клин: крыша с наклоном h ≤ x (плоскость −x + h ≤ 0) над квадратом 0…4
        var wedge = new ConvexBrush(0, 0, 0, 4, 4, 4,
        [
            new ConvexBrush.Plane(-1, 1, 0, 0), new ConvexBrush.Plane(0, -1, 0, 0),
            new ConvexBrush.Plane(-1, 0, 0, 0), new ConvexBrush.Plane(1, 0, 0, 4),
            new ConvexBrush.Plane(0, 0, -1, 0), new ConvexBrush.Plane(0, 0, 1, 4),
        ]);

        Assert.Equal((0f, 1f), wedge.VerticalSpan(1, 2));
        Assert.Equal((0f, 3f), wedge.VerticalSpan(3, 2));
        Assert.Null(wedge.VerticalSpan(5, 2));
    }

    [Fact]
    public void RejectsUnknownFormat()
    {
        var data = Model();
        data[9] = 7; // версия модели 0x10000007

        Assert.Throws<InvalidDataException>(() => SceneObstacles.ReadModel(data));
        Assert.Throws<EndOfStreamException>(() => SceneObstacles.ReadModel(Model().Take(500).ToArray()));
    }

    [Fact]
    public void NumbersTilesFromNorthWest()
    {
        // Мир 8×11: северо-западный квадрат — 1, юго-восточный — 88; ClaudeCot у камней (проверено 09.11) — 62
        Assert.Equal(1, SceneObstacles.TileNumber(-4000, 5600, 11, 8));
        Assert.Equal(88, SceneObstacles.TileNumber(4000, -5600, 11, 8));
        Assert.Equal(62, SceneObstacles.TileNumber(1634.9f, -1895.8f, 11, 8));
        Assert.Null(SceneObstacles.TileNumber(5000, 0, 11, 8));
    }

    [Fact]
    public void RealClientHasRocksNearFarmSpot()
    {
        Assert.SkipUnless(Directory.Exists(ComebackDirectory), $"Не задана {ComebackVariable} — папка element клиента Comeback 1.4.6");

        var format = new ProfileCatalog().Load("comeback146").Data.GameFiles.Pck;
        var obstacles = SceneObstacles.LoadFromGameDirectory(ComebackDirectory, "world", [62], format, out var problem);

        Assert.Null(problem);
        Assert.True(obstacles.Brushes.Count > 1000);
        // Камень в 6 м к северо-западу от места ClaudeCot (1634,9; −1895,8; ноги 220,9) — сверено со скриншотом владельца
        Assert.True(obstacles.Blocks(1631.5f, -1886.8f, 218.9f, 223.4f));
        Assert.False(obstacles.Blocks(1634.9f, -1895.8f, 218.9f, 223.4f));
    }
}

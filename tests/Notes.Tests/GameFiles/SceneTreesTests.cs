using System;
using System.IO;
using System.Linq;
using System.Text;
using Notes.Core.GameFiles;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.GameFiles;

public class SceneTreesTests
{
    // Тест на файлах настоящего клиента: папка element — в переменной окружения. Нет переменной — тест пропускается
    private const string Variable = "NOTES_COMEBACK136_DIR";
    private static readonly string Directory = Environment.GetEnvironmentVariable(Variable) ?? "";

    // Кирпич-куб с углами (x0, h0, y0) и (x1, h1, y1), как в файле
    private static void Brush(BinaryWriter w, float x0, float h0, float y0, float x1, float h1, float y1)
    {
        foreach (var f in new[] { (x0 + x1) / 2, (h0 + h1) / 2, (y0 + y1) / 2, (x1 - x0) / 2, (h1 - h0) / 2, (y1 - y0) / 2, x0, h0, y0, x1, h1, y1 })
            w.Write(f);
        w.Write(0);
        var planes = new[] { (-1f, 0f, 0f, -x0), (1f, 0f, 0f, x1), (0f, -1f, 0f, -h0), (0f, 1f, 0f, h1), (0f, 0f, -1f, -y0), (0f, 0f, 1f, y1) };
        w.Write(planes.Length);
        foreach (var (x, h, y, d) in planes)
        {
            w.Write(x);
            w.Write(h);
            w.Write(y);
            w.Write(d);
            w.Write((byte)0);
        }
    }

    // Форма дерева: ствол 1×1 м до высоты 6 м и крона выше 3 м
    private static byte[] Shape()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes("MOXB"));
        w.Write(9);
        w.Write(2);
        foreach (var (x0, h0, y0, x1, h1, y1) in new[] { (-0.5f, 0f, -0.5f, 0.5f, 6f, 0.5f), (-3f, 3f, -3f, 3f, 8f, 3f) })
        {
            w.Write(Encoding.ASCII.GetBytes("V0.2======Convex_Hull_Data_Start========="));
            w.Write(0);
            w.Write(5);
            w.Write(new byte[5]);
            Brush(w, x0, h0, y0, x1, h1, y1);
        }

        return stream.ToArray();
    }

    // Сцена: карта 128×128 м, блоки по 64 м (2×2), один вид дерева, в северо-западном блоке два дерева
    private static byte[] Scene()
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        var kind = PckArchive.Gbk.GetBytes("Trees\\树\\Oak.spt");
        const int blocks = 0x74;
        const int kinds = blocks + 4 * 4;
        var block = kinds + 4 + kind.Length + 4 + 1;
        w.Write(0x45435744);
        w.Write(12);
        w.Write(128f);
        w.Write(128f);
        w.Write(64f);
        w.Write(32);
        w.Write(4);
        w.Write(2);
        w.Write(2);
        w.Write(kinds);
        w.Write(0);
        w.Write(1);
        w.Write(new byte[blocks - 0x30]);
        w.Write(block);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(kind.Length);
        w.Write(kind);
        w.Write(1);
        w.Write((byte)'x');
        w.Write(0);
        w.Write(2);
        w.Write(new byte[9 * 4]);
        foreach (var (x, h, y) in new[] { (-30f, 10f, 30f), (-10f, 12f, 50f) })
        {
            w.Write(0);
            w.Write(x);
            w.Write(h);
            w.Write(y);
        }

        return stream.ToArray();
    }

    [Fact]
    public void ReadsShapeSkippingHullRecords()
    {
        var brushes = SceneTrees.ReadShape(Shape());

        Assert.Equal(2, brushes.Count);
        Assert.Equal(6f, brushes[0].MaxHeight);
        Assert.Equal(-3f, brushes[1].MinX);
    }

    [Fact]
    public void MovesBrushToTreePlace()
    {
        var trunk = SceneTrees.ReadShape(Shape())[0].Moved(100, 20, -50);

        Assert.Equal((20f, 26f), trunk.VerticalSpan(100, -50));
        Assert.Null(trunk.VerticalSpan(101, -50));
    }

    [Fact]
    public void FindsTreesInBlocksOfRectangle()
    {
        var scene = SceneTrees.ReadScene(Scene());

        Assert.Equal("Trees\\树\\Oak.spt", Assert.Single(scene.Kinds));
        Assert.Equal(2, scene.TreesIn(-40, 20, -20, 40).Count);
        Assert.Contains(new SceneTrees.Tree(0, -10, 12, 50), scene.TreesIn(-40, 20, -20, 40));
        Assert.Empty(scene.TreesIn(10, -40, 40, -10));
    }

    [Fact]
    public void ShapeFileIsNextToSpeedTreeFile()
    {
        Assert.Equal("trees\\树\\oak.tbh", SceneTrees.ShapePath("Trees\\树\\Oak.spt"));
        Assert.Equal("", SceneTrees.ShapePath("Trees\\Oak.dds"));
    }

    [Fact]
    public void RealClientHasTreesNearTestSpot()
    {
        Assert.SkipUnless(System.IO.Directory.Exists(Directory), $"Не задана {Variable} — папка element клиента Comeback 1.3.6");

        var format = new ProfileCatalog().Load("comeback136").Data.GameFiles.Pck;
        // Двор у домов, где 09.11 проверяли обход (окно «Незаметно»): рядом несколько деревьев
        var trees = SceneTrees.LoadFromGameDirectory(Directory, "world", 870, 3125, 970, 3225, format, out var problem);

        Assert.Null(problem);
        Assert.True(trees.TreeCount >= 5);
        Assert.NotEmpty(trees.Brushes);
        // Формы сдвинуты на места деревьев: деревья берутся блоками по 64 м, поэтому — в блоках вокруг прямоугольника, на высоте
        // земли округи (≈ 210–240)
        Assert.All(trees.Brushes, b => Assert.True(b.MinX > 832 && b.MaxX < 1024 && b.MinY > 3072 && b.MaxY < 3264 && b.MinHeight is > 200 and < 250));
    }
}

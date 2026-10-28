using System;
using System.IO;
using System.Text;
using Notes.Core.GameFiles;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.GameFiles;

public class IconAtlasTests
{
    private const string ComebackVariable = "NOTES_COMEBACK146_DIR";
    private static readonly string ComebackDirectory = Environment.GetEnvironmentVariable(ComebackVariable) ?? "";
    private static readonly Encoding Gbk = Encoding.GetEncoding(936);

    // Иконки 4×4, сетка 2×1: два блока DXT1
    private static byte[] Names(params string[] files) => Gbk.GetBytes("4\r\n4\r\n1\r\n2\r\n" + string.Join("\r\n", files) + "\r\n");

    private static byte[] Dds(params (ushort C0, ushort C1, uint Bits)[] blocks)
    {
        var dds = new byte[128 + blocks.Length * 8];
        BitConverter.GetBytes(0x20534444u).CopyTo(dds, 0);
        BitConverter.GetBytes(4).CopyTo(dds, 12);
        BitConverter.GetBytes(blocks.Length * 4).CopyTo(dds, 16);
        Encoding.ASCII.GetBytes("DXT1").CopyTo(dds, 84);
        for (var i = 0; i < blocks.Length; i++)
        {
            BitConverter.GetBytes(blocks[i].C0).CopyTo(dds, 128 + i * 8);
            BitConverter.GetBytes(blocks[i].C1).CopyTo(dds, 130 + i * 8);
            BitConverter.GetBytes(blocks[i].Bits).CopyTo(dds, 132 + i * 8);
        }

        return dds;
    }

    // Пиксель BGRA
    private static byte[] Pixel(IconImage icon, int n) => [icon.Pixels[n * 4], icon.Pixels[n * 4 + 1], icon.Pixels[n * 4 + 2], icon.Pixels[n * 4 + 3]];

    [Fact]
    public void IconIsCellOfItsName()
    {
        // Красный и синий: первый блок весь красный (индекс 0), у второго первый цвет не больше второго — индекс 3 прозрачный
        var atlas = IconAtlas.Parse(Names("unknown.dds", "治疗宠物.dds"), Dds((0xF800, 0x001F, 0), (0x0000, 0xFFFF, 0xFFFFFFFF)));

        Assert.Equal(2, atlas.Count);
        var red = atlas.Get("UNKNOWN.DDS")!;
        Assert.Equal(4, red.Width);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, Pixel(red, 15));
        Assert.Equal(0, Pixel(atlas.Get("治疗宠物.dds")!, 0)[3]);
        Assert.Null(atlas.Get("нет.dds"));
    }

    [Fact]
    public void BlockMixesItsTwoColors()
    {
        // Пиксель 0 — индекс 2: две трети первого цвета (красный) и треть второго (синий)
        var atlas = IconAtlas.Parse(Names("a.dds"), Dds((0xF800, 0x001F, 2)));

        Assert.Equal(new byte[] { 85, 0, 170, 255 }, Pixel(atlas.Get("a.dds")!, 0));
    }

    [Fact]
    public void NotDxt1IsReported()
    {
        var dds = Dds((0, 0, 0));
        Encoding.ASCII.GetBytes("DXT5").CopyTo(dds, 84);

        Assert.Throws<InvalidDataException>(() => IconAtlas.Parse(Names("a.dds"), dds));
    }

    [Fact]
    public void RealComebackSkillIcons()
    {
        Assert.SkipUnless(Directory.Exists(ComebackDirectory), $"Не задана {ComebackVariable} — папка element клиента Comeback 1.4.6");

        var pck = new ProfileCatalog().Load("comeback146").Data.GameFiles.Pck;
        var atlas = IconAtlas.LoadFromGameDirectory(ComebackDirectory, "skill", pck, out var problem);

        Assert.Null(problem);
        Assert.True(atlas.Count > 1000);
        Assert.Equal(32, atlas.Get("治疗宠物.dds")?.Width);
    }
}

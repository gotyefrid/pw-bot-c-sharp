using System;
using System.Text;
using BotCH.Core.GameFiles;
using BotCH.Core.Memory;
using Xunit;

namespace BotCH.Tests.GameFiles;

public class SkillIconsTests
{
    private const uint Library = 0x1000_0000;
    private const uint Constants = Library + 0x1000;
    private const uint Data = Library + 0x2000;
    private static readonly Encoding Gbk = Encoding.GetEncoding(936);

    // Библиотека в памяти: заголовки MZ/PE и две секции — постоянные данные (таблицы методов, строки) и данные с описаниями
    private static MemoryImage LibraryImage()
    {
        var memory = new MemoryImage();
        memory.Map(Library, 0x3000);
        memory.WriteBytes(Library, [0x4D, 0x5A]);
        memory.WriteUInt32(Library + 0x3C, 0x80);
        memory.WriteUInt32(Library + 0x80, 0x00004550);
        memory.WriteBytes(Library + 0x86, BitConverter.GetBytes((ushort)2));
        memory.WriteBytes(Library + 0x94, BitConverter.GetBytes((ushort)0xE0));
        Section(memory, Library + 0x178, ".rdata", 0x1000, 0x40000040);
        Section(memory, Library + 0x1A0, ".data", 0x2000, 0xC0000040);
        return memory;
    }

    private static void Section(MemoryImage memory, uint header, string name, uint address, uint characteristics)
    {
        memory.WriteBytes(header, Encoding.ASCII.GetBytes(name));
        memory.WriteUInt32(header + 8, 0x1000);
        memory.WriteUInt32(header + 12, address);
        memory.WriteUInt32(header + 36, characteristics);
    }

    private static uint Text(MemoryImage memory, uint address, string text)
    {
        memory.WriteBytes(address, [.. Gbk.GetBytes(text), 0]);
        return address;
    }

    private static void Describe(MemoryImage memory, uint address, uint methods, int id, uint icon)
    {
        memory.WriteUInt32(address, methods);
        memory.WriteUInt32(address + 4, (uint)id);
        memory.WriteUInt32(address + 0x14, icon);
    }

    [Fact]
    public void IconFileComesFromSkillDescription()
    {
        // Где в описании номер, бот находит сам: на этом расстоянии от указателя на иконку больше всех разных номеров
        var memory = LibraryImage();
        var methods = Constants;
        var poison = Text(memory, Constants + 0x100, "剧毒蛊.dds");
        var heal = Text(memory, Constants + 0x120, "治疗宠物.dds");
        var portal = Text(memory, Constants + 0x140, "水煞.dds");
        var model = Text(memory, Constants + 0x160, "模型.ecm");
        Describe(memory, Data, methods, 299, poison);
        Describe(memory, Data + 0xA0, methods, 330, heal);
        Describe(memory, Data + 0x140, methods, 167, portal);
        // Не описания: таблица методов не в постоянных данных, имя не картинки, номер вне 1..65535
        Describe(memory, Data + 0x200, Data, 500, heal);
        Describe(memory, Data + 0x300, methods, 501, model);
        Describe(memory, Data + 0x400, methods, 70000, heal);

        var files = SkillIcons.ReadFiles(memory, Library);

        Assert.Equal(3, files.Count);
        Assert.Equal("剧毒蛊.dds", files[299]);
        Assert.Equal("治疗宠物.dds", files[330]);
        Assert.Equal("水煞.dds", files[167]);
    }

    [Fact]
    public void FirstDescriptionOfSkillWins()
    {
        var memory = LibraryImage();
        Describe(memory, Data, Constants, 299, Text(memory, Constants + 0x100, "剧毒蛊.dds"));
        Describe(memory, Data + 0xA0, Constants, 299, Text(memory, Constants + 0x120, "другая.dds"));

        Assert.Equal("剧毒蛊.dds", SkillIcons.ReadFiles(memory, Library)[299]);
    }

    [Fact]
    public void UnclearLayoutGivesNoIcons()
    {
        // Номер с таблицей методов перед ним — на двух расстояниях от иконки поровну: неясно, где он, — лучше без иконок
        var memory = LibraryImage();
        var icon = Text(memory, Constants + 0x100, "剧毒蛊.dds");
        memory.WriteUInt32(Data + 0x14, icon);
        memory.WriteUInt32(Data, Constants);
        memory.WriteUInt32(Data + 4, 299);
        memory.WriteUInt32(Data + 0x1C, Constants);
        memory.WriteUInt32(Data + 0x20, 330);

        Assert.Empty(SkillIcons.ReadFiles(memory, Library));
    }
}

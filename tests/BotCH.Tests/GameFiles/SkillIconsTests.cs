using System;
using System.Text;
using BotCH.Core.GameFiles;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.GameFiles;

public class SkillIconsTests
{
    private const uint Library = 0x1000_0000;
    private const uint Constants = Library + 0x1000;
    private const uint Data = Library + 0x2000;
    private static readonly Encoding Gbk = Encoding.GetEncoding(936);
    private static readonly SkillStubOffsets Stub = new() { Id = 0x4, Icon = 0x14 };

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
        var memory = LibraryImage();
        var methods = Constants;
        var poison = Text(memory, Constants + 0x100, "剧毒蛊.dds");
        var heal = Text(memory, Constants + 0x120, "治疗宠物.dds");
        var model = Text(memory, Constants + 0x140, "模型.ecm");
        Describe(memory, Data, methods, 299, poison);
        Describe(memory, Data + 0xA0, methods, 330, heal);
        // Не описания: таблица методов не в постоянных данных, имя не картинки, номер вне 1..65535
        Describe(memory, Data + 0x200, Data, 500, heal);
        Describe(memory, Data + 0x300, methods, 501, model);
        Describe(memory, Data + 0x400, methods, 70000, heal);

        var files = SkillIcons.ReadFiles(memory, Library, Stub);

        Assert.Equal(2, files.Count);
        Assert.Equal("剧毒蛊.dds", files[299]);
        Assert.Equal("治疗宠物.dds", files[330]);
    }

    [Fact]
    public void FirstDescriptionOfSkillWins()
    {
        var memory = LibraryImage();
        Describe(memory, Data, Constants, 299, Text(memory, Constants + 0x100, "剧毒蛊.dds"));
        Describe(memory, Data + 0xA0, Constants, 299, Text(memory, Constants + 0x120, "другая.dds"));

        Assert.Equal("剧毒蛊.dds", SkillIcons.ReadFiles(memory, Library, Stub)[299]);
    }
}

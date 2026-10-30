using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Memory;
using Xunit;

namespace Notes.Tests.Memory;

public class ServerCodeMapTests
{
    private const uint Head = 0x1000;
    private const uint HeadNode = 0x2000;
    private const uint OwnKeyMin = 0xFFEA;

    private readonly MemoryImage _image = new();

    /// <summary>Кладёт узел карты: left/parent/right, ключ, значение, флаги. isNil — «пустой» узел std::map.</summary>
    private void Node(uint address, uint left, uint parent, uint right, ushort key, uint value, byte flags, bool isNil = false)
    {
        var raw = new byte[0x18];
        BitConverter.GetBytes(left).CopyTo(raw, 0x00);
        BitConverter.GetBytes(parent).CopyTo(raw, 0x04);
        BitConverter.GetBytes(right).CopyTo(raw, 0x08);
        raw[0x0D] = isNil ? (byte)1 : (byte)0;
        BitConverter.GetBytes(key).CopyTo(raw, 0x0E);
        BitConverter.GetBytes(value).CopyTo(raw, 0x10);
        raw[0x14] = flags;
        _image.WriteBytes(address, raw);
    }

    [Fact]
    public void ReadsTheWholeTreeFromRootNotFromMinimum()
    {
        // Голова: left — минимум (1), parent — корень (5), right — максимум (9)
        _image.WriteUInt32(Head, HeadNode);
        Node(HeadNode, 0x2100, 0x2300, 0x2200, 99, 0, 0x00);

        // Левая ветка от корня: 1 → 3
        Node(0x2300, 0x2140, HeadNode, 0x2200, 5, 0x5000, 0x03);
        Node(0x2140, 0, 0x2300, 0x2180, 1, 0x1000, 0x03);
        Node(0x2180, 0, 0x2140, 0, 3, 0x3000, 0x03);
        Node(0x2200, 0, 0x2300, 0x2240, 9, 0x9000, 0x03);
        Node(0x2240, 0, 0x2200, 0, 200, 0x2000, 0x0B);

        var entries = ServerCodeMap.Read(_image, Head, OwnKeyMin, codeBytes: 0);

        Assert.Equal([1u, 3u, 5u, 9u, 200u], entries.Select(e => e.Key));
        Assert.All(entries, e => Assert.False(e.Own));
    }

    [Fact]
    public void EmptyMapHasNoEntries()
    {
        _image.WriteUInt32(Head, HeadNode);
        Node(HeadNode, HeadNode, HeadNode, HeadNode, 0, 0, 0x00, isNil: true);

        // Корень — сама голова (parent указывает на неё же): ни одного узла
        Assert.Empty(ServerCodeMap.Read(_image, Head, OwnKeyMin, codeBytes: 0));
    }

    [Fact]
    public void OwnKeysAreMarked()
    {
        _image.WriteUInt32(Head, HeadNode);
        Node(HeadNode, 0x2100, 0x2100, 0x2110, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0x2110, 0xFFEA, 0x5000, 0x02);
        Node(0x2110, 0, 0x2100, 0, 5, 0x6000, 0x02);

        var entries = ServerCodeMap.Read(_image, Head, OwnKeyMin, codeBytes: 0);

        Assert.True(entries.Single(e => e.Key == 0xFFEA).Own);
        Assert.False(entries.Single(e => e.Key == 5).Own);
    }

    [Fact]
    public void MissingHeadThrows()
    {
        // По адресу головы ничего не лежит — значит, смещение в профиле не отсюда
        Assert.Throws<MemoryAccessException>(() => ServerCodeMap.Read(_image, Head, OwnKeyMin, codeBytes: 0));
    }

    [Fact]
    public void ZeroMapHeadIsNotConfigured()
        => Assert.Throws<InvalidOperationException>(() => ServerCodeMap.Read(_image, 0, OwnKeyMin, codeBytes: 0));

    [Fact]
    public void StringsComeFromPointersInsideTheCode()
    {
        // Клиент кладёт рядом код и строки: по смещению 0x10 — «user32.dll», по 0x1C — «FindWindowA»
        var code = new byte[0x40];
        BitConverter.GetBytes(0x1010u).CopyTo(code, 0x00);
        BitConverter.GetBytes(0x101Cu).CopyTo(code, 0x04);
        "user32.dll\0"u8.ToArray().CopyTo(code, 0x10);
        "FindWindowA\0"u8.ToArray().CopyTo(code, 0x1C);

        var strings = ServerCodeMap.Strings(code, 0x1000);

        Assert.Equal(["user32.dll", "FindWindowA"], strings);
    }

    [Fact]
    public void PointersOutsideTheBufferAreIgnored()
    {
        var code = new byte[0x40];
        BitConverter.GetBytes(0x9999u).CopyTo(code, 0x00);  // далеко за буфером
        BitConverter.GetBytes(0x0FF0u).CopyTo(code, 0x04);  // до начала буфера
        "user32.dll\0"u8.ToArray().CopyTo(code, 0x10);
        BitConverter.GetBytes(0x1010u).CopyTo(code, 0x08);

        Assert.Equal(["user32.dll"], ServerCodeMap.Strings(code, 0x1000));
    }

    [Fact]
    public void ShortRunsAreNotStrings()
    {
        var code = new byte[0x20];
        BitConverter.GetBytes(0x1010u).CopyTo(code, 0x00);
        "abc\0"u8.ToArray().CopyTo(code, 0x10); // короче 5 символов — обрывок данных

        Assert.Empty(ServerCodeMap.Strings(code, 0x1000));
    }

    [Fact]
    public void HexIsSpacedBytes()
    {
        Assert.Equal("55 8B EC", ServerCodeMap.Hex([0x55, 0x8B, 0xEC, 0x90], 3));
        Assert.Equal("", ServerCodeMap.Hex([0x55], 0));
    }
}

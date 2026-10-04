using BotCH.Core.Memory;
using Xunit;

namespace BotCH.Tests.Memory;

public class MemoryImageTests
{
    [Fact]
    public void WrittenBytesReadBack()
    {
        var image = new MemoryImage();
        image.WriteBytes(0x400000, [1, 2, 3, 4]);

        Assert.Equal([1, 2, 3, 4], image.ReadBytes(0x400000, 4));
    }

    [Fact]
    public void UnmappedAddressIsUnreadable()
    {
        var image = new MemoryImage();
        image.WriteBytes(0x400000, [1]);

        Assert.False(image.TryRead(0x500000, new byte[4], 4));
    }

    [Fact]
    public void RestOfWrittenPageReadsAsZeros()
    {
        var image = new MemoryImage();
        image.WriteBytes(0x400000, [0xFF]);

        Assert.Equal(0u, image.ReadUInt32(0x400100));
    }

    [Fact]
    public void ReadAcrossTwoPages()
    {
        var image = new MemoryImage();
        image.WriteBytes(0x400FFE, [1, 2, 3, 4]);

        Assert.Equal([1, 2, 3, 4], image.ReadBytes(0x400FFE, 4));
    }

    [Fact]
    public void ReadFailsIfSecondPageIsMissing()
    {
        var image = new MemoryImage();
        image.Map(0x400000, MemoryImage.PageSize);

        Assert.False(image.TryRead(0x400FFE, new byte[4], 4));
    }

    [Fact]
    public void ReadPastEndOfAddressSpaceFails()
    {
        var image = new MemoryImage();
        image.Map(0xFFFFF000, MemoryImage.PageSize);

        Assert.False(image.TryRead(0xFFFFFFFE, new byte[4], 4));
    }
}

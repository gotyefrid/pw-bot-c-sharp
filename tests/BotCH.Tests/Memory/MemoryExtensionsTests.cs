using System;
using System.Text;
using BotCH.Core.Memory;
using Xunit;

namespace BotCH.Tests.Memory;

public class MemoryExtensionsTests
{
    private readonly MemoryImage _image = new();

    [Fact]
    public void NumbersAreLittleEndian()
    {
        _image.WriteBytes(0x1000, [0x78, 0x56, 0x34, 0x12]);

        Assert.Equal(0x12345678u, _image.ReadUInt32(0x1000));
        Assert.Equal(0x12345678, _image.ReadInt32(0x1000));
        Assert.Equal((ushort)0x5678, _image.ReadUInt16(0x1000));
        Assert.Equal((byte)0x78, _image.ReadByte(0x1000));
    }

    [Fact]
    public void ReadsFloat()
    {
        _image.WriteBytes(0x1000, BitConverter.GetBytes(-123.5f));

        Assert.Equal(-123.5f, _image.ReadFloat(0x1000));
    }

    [Fact]
    public void ReadFromUnmappedAddressThrowsWithAddress()
    {
        var error = Assert.Throws<MemoryAccessException>(() => _image.ReadUInt32(0xDEAD0000));

        Assert.Equal(0xDEAD0000u, error.Address);
        Assert.Contains("0xDEAD0000", error.Message);
    }

    [Fact]
    public void TryReadUInt32ReportsFailure()
    {
        Assert.False(_image.TryReadUInt32(0xDEAD0000, out _));
    }

    [Fact]
    public void UnicodeStringStopsAtZero()
    {
        _image.WriteBytes(0x2000, Encoding.Unicode.GetBytes("Друид\0мусор"));

        Assert.Equal("Друид", _image.ReadUnicodeString(0x2000));
    }

    [Fact]
    public void UnicodeStringIsLimitedByMaxChars()
    {
        _image.WriteBytes(0x2000, Encoding.Unicode.GetBytes("ОченьДлинныйНик"));

        Assert.Equal("Очень", _image.ReadUnicodeString(0x2000, maxChars: 5));
    }

    [Fact]
    public void UnicodeStringAtEndOfMappedMemory()
    {
        // Ник в самом конце доступной памяти: целиком 64 символа не прочитать, но строку получить надо
        _image.WriteBytes(0x2FF8, Encoding.Unicode.GetBytes("Ник\0"));

        Assert.Equal("Ник", _image.ReadUnicodeString(0x2FF8));
    }

    [Fact]
    public void FollowPointersLikeGameBase()
    {
        // [[module+0x5B3EEC]+0x1C] = game, [game+0x20] = персонаж
        const uint module = 0x400000, baseObject = 0x10000000, game = 0x20000000, pers = 0x30000000;
        _image.WriteUInt32(module + 0x5B3EEC, baseObject);
        _image.WriteUInt32(baseObject + 0x1C, game);
        _image.WriteUInt32(game + 0x20, pers);

        Assert.Equal(baseObject + 0x1C, _image.FollowPointers(module + 0x5B3EEC, 0x1C));
        Assert.Equal(game, _image.ReadPointerChain(module + 0x5B3EEC, 0x1C));
        Assert.Equal(pers, _image.ReadPointerChain(module + 0x5B3EEC, 0x1C, 0x20));
    }

    [Fact]
    public void FollowPointersWithoutOffsetsReturnsStart()
    {
        Assert.Equal(0x1234u, _image.FollowPointers(0x1234));
    }

    [Fact]
    public void NullPointerInChainThrows()
    {
        _image.WriteUInt32(0x1000, 0); // объекта ещё нет

        var error = Assert.Throws<MemoryAccessException>(() => _image.ReadPointerChain(0x1000, 0x20));
        Assert.Contains("Нулевой указатель", error.Message);
    }
}

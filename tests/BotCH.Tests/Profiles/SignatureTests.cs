using System;
using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.Profiles;

public class SignatureTests
{
    [Fact]
    public void WildcardMatchesAnyByte()
    {
        var signature = Signature.Parse("56 6A ?? E8");

        Assert.True(signature.Matches([0x56, 0x6A, 0x06, 0xE8]));
        Assert.True(signature.Matches([0x56, 0x6A, 0xFF, 0xE8]));
        Assert.False(signature.Matches([0x56, 0x6B, 0x06, 0xE8]));
    }

    [Fact]
    public void ShortDataDoesNotMatch()
    {
        Assert.False(Signature.Parse("56 6A 06 E8").Matches([0x56, 0x6A]));
    }

    [Fact]
    public void FindAllReturnsEveryPosition()
    {
        byte[] data = [0x90, 0x56, 0x6A, 0x01, 0x90, 0x56, 0x6A, 0x02, 0x56];

        Assert.Equal([1, 5], Signature.Parse("56 6A ??").FindAll(data));
    }

    [Fact]
    public void FindAllWithLeadingWildcard()
    {
        byte[] data = [0x00, 0x6A, 0x01, 0x6A];

        Assert.Equal([0, 2], Signature.Parse("?? 6A").FindAll(data));
    }

    [Fact]
    public void FindAllStopsAtLimit()
    {
        var data = new byte[] { 0x90, 0x90, 0x90, 0x90 };

        Assert.Equal(2, Signature.Parse("90").FindAll(data, limit: 2).Count);
    }

    [Fact]
    public void PatternAtVeryEnd()
    {
        Assert.Equal([2], Signature.Parse("C3").FindAll([0x90, 0x90, 0xC3]));
    }

    [Fact]
    public void ToStringRoundTrips()
    {
        Assert.Equal("56 6A ?? E8", Signature.Parse("56  6a ? e8").ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?? ??")]
    [InlineData("56 XZ")]
    public void InvalidSignaturesAreRejected(string text)
    {
        Assert.Throws<FormatException>(() => Signature.Parse(text));
    }
}

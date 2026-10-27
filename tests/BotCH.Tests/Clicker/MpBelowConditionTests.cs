using BotCH.Core.Clicker;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «MP персонажа меньше, %».</summary>
public class MpBelowConditionTests
{
    private static string? Check(int mp, int? maxMp, int percent, int hp = 400)
        => new MpBelowCondition { Percent = percent }.Check(ClickerScript.Context(new FakeWorld { Mp = mp, MaxMp = maxMp, Hp = hp }.Snapshot()));

    [Fact]
    public void HoldsBelowThreshold() => Assert.Equal("MP 20 % — меньше 30 %", Check(100, 500, 30));

    [Theory]
    [InlineData(150, 500)]
    [InlineData(500, 500)]
    public void NotAtOrAboveThreshold(int mp, int maxMp) => Assert.Null(Check(mp, maxMp, 30));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void UnknownMaxMpIsNotLowMp(int? maxMp) => Assert.Null(Check(0, maxMp, 30));

    [Fact]
    public void LooksAtMpNotHp() => Assert.Null(Check(500, 500, 30, hp: 10));

    [Theory]
    [InlineData(40, 40)]
    [InlineData(500, 100)]
    [InlineData(0, 1)]
    public void FileKeepsPercentWithinLimits(int written, int read)
        => Assert.Equal(read, ClickerScript.RoundTrip(new MpBelowCondition { Percent = written }).Percent);

    [Fact]
    public void Described() => Assert.Equal("MP меньше 30 %", new MpBelowCondition { Percent = 30 }.Describe());
}

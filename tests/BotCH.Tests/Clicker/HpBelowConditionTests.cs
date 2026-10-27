using BotCH.Core.Clicker;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «HP персонажа меньше, %».</summary>
public class HpBelowConditionTests
{
    private static string? Check(int hp, int maxHp, int percent)
        => new HpBelowCondition { Percent = percent }.Check(ClickerScript.Context(new FakeWorld { Hp = hp, MaxHp = maxHp }.Snapshot()));

    [Fact]
    public void HoldsBelowThreshold() => Assert.Equal("HP 48 % — меньше 50 %", Check(240, 500, 50));

    [Theory]
    [InlineData(250, 500)]
    [InlineData(400, 500)]
    public void NotAtOrAboveThreshold(int hp, int maxHp) => Assert.Null(Check(hp, maxHp, 50));

    [Fact]
    public void UnknownMaxHpIsNotLowHp() => Assert.Null(Check(0, 0, 50));

    [Theory]
    [InlineData(40, 40)]
    [InlineData(500, 100)]
    [InlineData(0, 1)]
    public void FileKeepsPercentWithinLimits(int written, int read)
        => Assert.Equal(read, ClickerScript.RoundTrip(new HpBelowCondition { Percent = written }).Percent);

    [Fact]
    public void Described() => Assert.Equal("HP меньше 30 %", new HpBelowCondition { Percent = 30 }.Describe());
}

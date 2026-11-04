using Notes.Core.Clicker;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Условие «HP цели, %» (меньше, больше, равно): моб, игрок, член пати или сам персонаж в цели; цели нет или её максимум HP
/// неизвестен — не выполнено.
/// </summary>
public class TargetHpBelowConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(int percent) => new TargetHpBelowCondition { Percent = percent }.Check(ClickerScript.Context(_world.Snapshot()));

    private void MobInTarget(int hp, int maxHp)
    {
        _world.Npcs.Add(new NpcInfo(1, 1, NpcKind.Mob, NpcState.Standing, 0, new Position(5, 0, 0), "Волк", hp) { MaxHp = maxHp });
        _world.TargetWid = 1;
    }

    [Fact]
    public void MobBelowThreshold()
    {
        MobInTarget(25, 100);

        Assert.Equal("HP цели 25 % — меньше 50 %", Check(50));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    public void NotAtOrAboveThreshold(int hp)
    {
        MobInTarget(hp, 100);

        Assert.Null(Check(50));
    }

    [Fact]
    public void NoTargetOrUnknownMaxHpDoesNotHold()
    {
        Assert.Null(Check(50));

        MobInTarget(10, 0);
        Assert.Null(Check(50));
    }

    [Fact]
    public void PlayerAndPartyMemberAndSelf()
    {
        _world.Players.Add(new PlayerInfo(2, 2, "Чужой", new Position(3, 0, 0)) { Hp = 30, MaxHp = 100 });
        _world.TargetWid = 2;
        Assert.Equal("HP цели 30 % — меньше 50 %", Check(50));

        _world.Party = new PartyInfo(3, [new PartyMember(3, "Друг", 50, 200, 1000, 0, 0, null)]);
        _world.TargetWid = 3;
        Assert.Equal("HP цели 20 % — меньше 50 %", Check(50));

        _world.Hp = _world.MaxHp / 4;
        _world.TargetWid = FakeWorld.HostWid;
        Assert.Equal("HP цели 25 % — меньше 50 %", Check(50));
    }

    [Theory]
    [InlineData(PercentCompare.Below, 49, true)]
    [InlineData(PercentCompare.Below, 50, false)]
    [InlineData(PercentCompare.Above, 51, true)]
    [InlineData(PercentCompare.Above, 50, false)]
    [InlineData(PercentCompare.Equal, 50, true)]
    [InlineData(PercentCompare.Equal, 51, false)]
    public void Compares(PercentCompare compare, int hp, bool holds)
    {
        MobInTarget(hp, 100);

        var why = new TargetHpBelowCondition { Percent = 50, Compare = compare }.Check(ClickerScript.Context(_world.Snapshot()));
        Assert.Equal(holds, why is not null);
    }

    [Fact]
    public void AboveIsExplained()
    {
        MobInTarget(80, 100);

        Assert.Equal("HP цели 80 % — больше 50 %",
            new TargetHpBelowCondition { Percent = 50, Compare = PercentCompare.Above }.Check(ClickerScript.Context(_world.Snapshot())));
    }

    [Theory]
    [InlineData(40, 40)]
    [InlineData(500, 100)]
    [InlineData(-5, 0)]
    public void FileKeepsPercentWithinLimits(int written, int read)
        => Assert.Equal(read, ClickerScript.RoundTrip(new TargetHpBelowCondition { Percent = written }).Percent);

    [Fact]
    public void FileKeepsCompare()
    {
        Assert.Equal(PercentCompare.Equal, ClickerScript.RoundTrip(new TargetHpBelowCondition { Compare = PercentCompare.Equal }).Compare);
        Assert.Equal(PercentCompare.Below, ClickerScript.RoundTrip(new TargetHpBelowCondition()).Compare);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("HP цели меньше 40 %", new TargetHpBelowCondition { Percent = 40 }.Describe());
        Assert.Equal("HP цели больше 40 %", new TargetHpBelowCondition { Percent = 40, Compare = PercentCompare.Above }.Describe());
        Assert.Equal("HP цели равно 100 %", new TargetHpBelowCondition { Percent = 100, Compare = PercentCompare.Equal }.Describe());
    }
}

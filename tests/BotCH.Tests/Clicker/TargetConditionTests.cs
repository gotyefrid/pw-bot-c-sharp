using BotCH.Core.Clicker;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «Цель персонажа»: равно «цели нет» / «цель есть».</summary>
public class TargetConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(TargetIs @is) => new TargetCondition { Is = @is }.Check(ClickerScript.Context(_world.Snapshot()));

    [Fact]
    public void NoTarget()
    {
        Assert.Equal("цели нет", Check(TargetIs.None));
        Assert.Null(Check(TargetIs.Any));
    }

    [Fact]
    public void MobTargetIsNamed()
    {
        _world.TargetWid = _world.AddMob(0x80104298, "Волк", 10).Wid;

        Assert.Null(Check(TargetIs.None));
        Assert.Equal("цель — Волк", Check(TargetIs.Any));
    }

    [Fact]
    public void AnyTargetCounts()
    {
        // В таргете тот, кого нет среди мобов рядом (игрок, NPC вдали), — цель всё равно есть
        _world.TargetWid = 0x00AB0001;

        Assert.Null(Check(TargetIs.None));
        Assert.Equal("цель есть", Check(TargetIs.Any));
    }

    [Fact]
    public void NewConditionIsNoTarget() => Assert.Equal(TargetIs.None, new TargetCondition().Is);

    [Fact]
    public void FileKeepsChoiceAsWord()
    {
        Assert.Contains("\"is\": \"Any\"", ClickerScript.Json(new TargetCondition { Is = TargetIs.Any }));
        Assert.Equal(TargetIs.Any, ClickerScript.RoundTrip(new TargetCondition { Is = TargetIs.Any }).Is);
        Assert.Equal(TargetIs.None, ClickerScript.RoundTrip(new TargetCondition()).Is);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("цели нет", new TargetCondition().Describe());
        Assert.Equal("цель есть", new TargetCondition { Is = TargetIs.Any }.Describe());
    }
}

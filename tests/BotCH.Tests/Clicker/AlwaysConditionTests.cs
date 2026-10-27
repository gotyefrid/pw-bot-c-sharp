using BotCH.Core.Clicker;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «Всегда».</summary>
public class AlwaysConditionTests
{
    [Fact]
    public void AlwaysHolds()
    {
        var world = new FakeWorld { Hp = 0, MaxHp = 0 };

        Assert.Equal("всегда", new AlwaysCondition().Check(ClickerScript.Context(world.Snapshot())));
    }

    [Fact]
    public void Described() => Assert.Equal("всегда", new AlwaysCondition().Describe());
}

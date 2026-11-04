using Notes.Core.Clicker;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>Условие «Макрос идёт»: идёт — выполнено; не идёт или неизвестно (поле не найдено для сервера) — нет.</summary>
public class MacroRunningConditionTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(null, false)]
    public void HoldsOnlyWhenRunning(bool? running, bool holds)
    {
        var world = new Fakes.FakeWorld { MacroRunning = running };

        Assert.Equal(holds, new MacroRunningCondition().Check(ClickerScript.Context(world.Snapshot())) is not null);
    }

    [Fact]
    public void FileKeepsIt() => Assert.IsType<MacroRunningCondition>(ClickerScript.RoundTrip(new MacroRunningCondition()));

    [Fact]
    public void Described() => Assert.Equal("макрос идёт", new MacroRunningCondition().Describe());
}

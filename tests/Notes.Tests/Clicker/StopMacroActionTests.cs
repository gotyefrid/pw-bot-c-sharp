using Notes.Core.Clicker;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Остановить макрос»: идёт — останавливает и ждёт, пока не идёт; не идёт — ничего не шлёт; идёт ли, неизвестно —
/// шлёт, и сделано.
/// </summary>
public class StopMacroActionTests
{
    private readonly ClickerScript _script = new();

    [Fact]
    public void RunningMacroIsStoppedAndWaitedFor()
    {
        _script.World.MacroRunning = true;
        var bot = _script.Step(new StopMacroAction()).Key("F1").Start();

        Assert.Equal(["stop-macro"], _script.Run(bot, 1));

        _script.World.MacroRunning = false;
        Assert.Equal(["key F1"], _script.Run(bot, 1));
    }

    [Fact]
    public void NotRunningSendsNothing()
    {
        _script.World.MacroRunning = false;

        Assert.Equal(["key F1"], _script.Step(new StopMacroAction()).Key("F1").Run(_script.Start(), 1));
    }

    [Fact]
    public void UnknownIsSentAndDone()
    {
        var bot = _script.Step(new StopMacroAction()).Key("F1").Start();

        // Подтверждение — на следующем снимке, дальше клавиша и по кругу
        Assert.Equal(["stop-macro", "key F1, stop-macro"], _script.Run(bot, 2));
    }

    [Fact]
    public void FileKeepsIt() => Assert.IsType<StopMacroAction>(ClickerScript.RoundTrip(new StopMacroAction()));

    [Fact]
    public void Described() => Assert.Equal("остановить макрос", new StopMacroAction().Describe());
}

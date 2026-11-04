using Notes.Core.Clicker;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// «Выполнять не чаще, с» — общее у всех действий: раньше срока шаг пропускается, как с невыполненным условием; срок — от
/// начала прошлого выполнения.
/// </summary>
public class MinIntervalTests
{
    private readonly ClickerScript _script = new();

    [Fact]
    public void StepIsSkippedUntilIntervalPasses()
    {
        // Снимки раз в 0,25 с: F1 — на 1-м и 5-м (через 1 с), F2 — каждый ход
        var bot = _script.Step(new PressKeyAction { Key = "F1", MinIntervalSeconds = 1 }).Key("F2").Start();

        Assert.Equal(["key F1, key F2", "key F2", "key F2", "key F2", "key F1, key F2"], _script.Run(bot, 5));
    }

    [Fact]
    public void ZeroMeansEveryTime()
    {
        var bot = _script.Key("F1").Start();

        Assert.Equal(["key F1", "key F1"], _script.Run(bot, 2));
    }

    [Fact]
    public void TableShowsInterval()
    {
        Assert.Equal("клавиша F1", new PressKeyAction { Key = "F1" }.DescribeWithInterval());
        Assert.Equal("клавиша F1, не чаще раза в 5 с", new PressKeyAction { Key = "F1", MinIntervalSeconds = 5 }.DescribeWithInterval());
    }

    [Fact]
    public void FileHasItOnlyWhenSetAndKeepsItInLimits()
    {
        Assert.DoesNotContain("\"minIntervalSeconds\"", ClickerScript.Json(new PauseAction()));
        Assert.Equal(5, ClickerScript.RoundTrip(new PauseAction { MinIntervalSeconds = 5 }).MinIntervalSeconds);
        Assert.Equal(0, ClickerScript.RoundTrip(new PauseAction { MinIntervalSeconds = -3 }).MinIntervalSeconds);
        Assert.Equal(ClickerAction.MaxIntervalSeconds, ClickerScript.RoundTrip(new PauseAction { MinIntervalSeconds = int.MaxValue }).MinIntervalSeconds);
    }
}

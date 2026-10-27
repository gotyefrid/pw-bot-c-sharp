using BotCH.Core.Clicker;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Действие «Пауза»: время — в миллисекундах.</summary>
public class PauseActionTests
{
    [Fact]
    public void HoldsNextStepForItsTime()
    {
        // Снимки раз в 250 мс: пауза 500 мс — следующий шаг на втором снимке после неё
        var script = new ClickerScript().Pause(500).Key("F1");

        Assert.Equal(["", "", "key F1"], script.Run(script.Start(), 3));
    }

    [Fact]
    public void ZeroPauseDoesNotHold()
    {
        var script = new ClickerScript().Pause(0).Key("F1");

        Assert.Equal(["key F1"], script.Run(script.Start(), 1));
    }

    [Fact]
    public void ShortPauseWaitsForNextSnapshot()
    {
        // Время кликер сверяет по снимкам: 100 мс — это до следующего снимка (250 мс)
        var script = new ClickerScript().Pause(100).Key("F1");

        Assert.Equal(["", "key F1"], script.Run(script.Start(), 2));
    }

    [Theory]
    [InlineData(2500, 2500)]
    [InlineData(-3, 0)]
    [InlineData(int.MaxValue, PauseAction.MaxMilliseconds)]
    public void FileKeepsTimeWithinLimits(int written, int read)
        => Assert.Equal(read, ClickerScript.RoundTrip(new PauseAction { Milliseconds = written }).Milliseconds);

    [Fact]
    public void Described() => Assert.Equal("пауза 500 мс", new PauseAction { Milliseconds = 500 }.Describe());

    [Fact]
    public void AlwaysReady() => Assert.Null(new PauseAction().Problem);
}

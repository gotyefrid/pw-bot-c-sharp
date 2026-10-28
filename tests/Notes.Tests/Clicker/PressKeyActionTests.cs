using Notes.Core.Clicker;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>Действие «Нажать клавишу».</summary>
public class PressKeyActionTests
{
    [Fact]
    public void PressesKeyAndScriptGoesOnSameTurn()
    {
        var script = new ClickerScript().Key("Alt+1").Key("F2");

        Assert.Equal(["key Alt+1, key F2"], script.Run(script.Start(), 1));
    }

    [Fact]
    public void SameKeyTwiceInScriptIsTwoPresses()
    {
        var script = new ClickerScript().Key("F1").Key("F1");

        Assert.Equal(["key F1, key F1"], script.Run(script.Start(), 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Win+1")]
    [InlineData("Alt+F4")]
    public void NoKeyOrForbiddenIsNotReady(string key)
    {
        var action = new PressKeyAction { Key = key };

        Assert.Null(action.Stroke);
        Assert.Equal("клавиша не выбрана", action.Problem);
    }

    [Fact]
    public void FileKeepsKeyAndDropsBadOne()
    {
        Assert.Equal("Ctrl+F2", ClickerScript.RoundTrip(new PressKeyAction { Key = "ctrl+f2" }).Key);
        Assert.Equal("", ClickerScript.RoundTrip(new PressKeyAction { Key = "Alt+F4" }).Key);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("клавиша F1", new PressKeyAction { Key = "F1" }.Describe());
        Assert.Equal("клавиша не выбрана", new PressKeyAction().Describe());
    }

    [Fact]
    public void KeyEditIsReported()
    {
        var action = new PressKeyAction();
        var edits = 0;
        action.PropertyChanged += (_, _) => edits++;

        action.Key = "F3";
        action.Key = "F3";

        Assert.Equal(1, edits);
    }
}

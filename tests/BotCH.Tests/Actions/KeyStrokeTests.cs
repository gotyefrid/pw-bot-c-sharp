using BotCH.Core.Actions;
using BotCH.Core.Clients;
using BotCH.Core.Logging;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Actions;

/// <summary>Клавиша кликера: как пишется в настройках и как уходит в окно игры.</summary>
public class KeyStrokeTests
{
    [Theory]
    [InlineData("F1", 0x70, false, false, false)]
    [InlineData("3", 0x33, false, false, false)]
    [InlineData("Ctrl+2", 0x32, true, false, false)]
    [InlineData("Ctrl+Alt+Shift+Q", 0x51, true, true, true)]
    [InlineData("Num+", 0x6B, false, false, false)]
    [InlineData("Shift+Num+", 0x6B, false, false, true)]
    [InlineData("←", 0x25, false, false, false)]
    public void ParsesAndWritesBack(string text, int key, bool ctrl, bool alt, bool shift)
    {
        Assert.True(KeyStroke.TryParse(text, out var stroke));
        Assert.Equal(new KeyStroke(key, ctrl, alt, shift), stroke);
        Assert.Equal(text, stroke.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Win+1")]
    [InlineData("F25")]
    public void UnknownIsNotParsed(string? text) => Assert.False(KeyStroke.TryParse(text, out _));

    [Fact]
    public void CaseAndSpacesDoNotMatter()
    {
        Assert.True(KeyStroke.TryParse(" ctrl+f1 ", out var stroke));
        Assert.Equal("Ctrl+F1", stroke.ToString());
    }

    [Fact]
    public void WindowsCombinationsAreForbidden()
    {
        // Alt+F4 закрыла бы клиент, Alt+Space открыла бы меню окна — игра встала бы
        Assert.NotNull(new KeyStroke(0x73, Alt: true).Forbidden);
        Assert.NotNull(new KeyStroke(0x20, Alt: true).Forbidden);
        Assert.Null(new KeyStroke(0x73).Forbidden);
        Assert.Null(new KeyStroke(0x31, Alt: true).Forbidden);
        Assert.Null(new KeyStroke(0x73, Ctrl: true, Alt: true).Forbidden);
    }

    [Fact]
    public void ModifiersMakeDifferentPresses()
        => Assert.NotEqual(new KeyStroke(0x31).Code, new KeyStroke(0x31, Ctrl: true).Code);

    [Fact]
    public void MessageCarriesScanCodeAndUpBits()
    {
        // Как у настоящего нажатия «1» (скан-код 2): повтор 1; у отпускания — биты 30 и 31; стрелка — «расширенная»
        Assert.Equal(0x00020001, WindowKeys.LParam(0x31, 2, alt: false, up: false));
        Assert.Equal(unchecked((int)0xC0020001), WindowKeys.LParam(0x31, 2, alt: false, up: true));
        Assert.Equal(0x214B0001, WindowKeys.LParam(0x25, 0x4B, alt: true, up: false));
    }

    [Fact]
    public void KeyActionIsConfirmedRightAway()
    {
        var world = new FakeWorld();
        var keys = new FakeActions();
        var runner = new ActionRunner(new GameControl(keys, keys), NullLogger.Instance);
        var action = new KeyAction(new KeyStroke(0x70));

        Assert.True(runner.Submit(new ActionOwner("тест"), action, world.Snapshot()).Sent);
        var outcome = Assert.Single(runner.Update(world.Wait(0.25).Snapshot()));

        Assert.Equal(ActionStatus.Confirmed, outcome.Status);
        Assert.Equal(["key F1"], keys.Calls);
    }

    [Fact]
    public void WithoutKeysNothingIsPressed()
    {
        var world = new FakeWorld();
        var runner = new ActionRunner(new GameControl(new FakeActions()), NullLogger.Instance);

        Assert.False(runner.Submit(new ActionOwner("тест"), new KeyAction(new KeyStroke(0x70)), world.Snapshot()).Sent);
    }
}

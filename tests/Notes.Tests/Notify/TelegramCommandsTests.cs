using Notes.Core.Notify;
using Xunit;

namespace Notes.Tests.Notify;

/// <summary>Ответы на команды из Telegram (разметка HTML: ники — в &lt;code&gt;, копируются нажатием).</summary>
public class TelegramCommandsTests
{
    private static string? Answer(string text, params TelegramCharacter[] connected)
        => TelegramCommands.Answer(new TelegramMessage(1, text), connected);

    [Fact]
    public void ListShowsConnectedWithCopyableNicks()
    {
        var reply = Answer("/list", new("Странник", "Comeback 1.4.6", true), new("Ёжик", "Comeback 1.4.6", false));

        Assert.Equal("Подключены:\n🔴 <code>Ёжик</code> — Comeback 1.4.6, бот стоит\n🟢 <code>Странник</code> — Comeback 1.4.6, бот работает",
            reply);
    }

    [Fact]
    public void ListWithBotNameWorksToo() => Assert.StartsWith("Подключены", Answer("/list@notes_bot", new TelegramCharacter("Странник", "PW", true)));

    [Fact]
    public void ListWhenNobodyConnected() => Assert.Equal("Подключённых персонажей нет", Answer("/list"));

    [Fact]
    public void NickIsEscapedForHtml() => Assert.Contains("<code>&lt;Тот&gt;&amp;</code>", Answer("/list", new TelegramCharacter("<Тот>&", "PW", true)));

    [Fact]
    public void UnknownGetsHelp() => Assert.Equal(TelegramCommands.Help, Answer("/start"));
}

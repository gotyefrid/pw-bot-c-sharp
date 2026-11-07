using Notes.Core.Calls;
using Notes.Core.Notify;
using Xunit;

namespace Notes.Tests.Notify;

/// <summary>Команды из Telegram: что просят и тексты ответов (разметка HTML: ники — в &lt;code&gt;, копируются нажатием).</summary>
public class TelegramCommandsTests
{
    private static TelegramRequest Parse(string text, string? repliedTo = null)
        => TelegramCommands.Parse(new TelegramMessage(1, text, repliedTo is null ? null : 5, repliedTo));

    // Уведомление, каким Telegram отдаёт его в ответе — без разметки
    private static string Notice(string host, string from, string text)
        => TelegramCommands.Plain(ChatNotifier.Format(host, "лично", from, text));

    [Fact]
    public void ListShowsConnectedWithCopyableNicks()
    {
        var reply = TelegramCommands.List([new("Странник", "Comeback 1.4.6", true), new("Ёжик", "Comeback 1.4.6", false)]);

        Assert.Equal("Подключены:\n🔴 <code>Ёжик</code> — Comeback 1.4.6, бот стоит\n🟢 <code>Странник</code> — Comeback 1.4.6, бот работает",
            reply);
    }

    [Fact]
    public void ListWithBotNameWorksToo() => Assert.IsType<ListRequest>(Parse("/list@notes_bot"));

    [Fact]
    public void ListWhenNobodyConnected() => Assert.Equal("Подключённых персонажей нет", TelegramCommands.List([]));

    [Fact]
    public void NickIsEscapedForHtml()
        => Assert.Contains("<code>&lt;Тот&gt;&amp;</code>", TelegramCommands.List([new("<Тот>&", "PW", true)]));

    [Fact]
    public void ReplyToNoticeWhispersBackFromThatCharacter()
        => Assert.Equal(new WhisperRequest("Перс", "Тихоня", "и тебе привет"), Parse("и тебе\nпривет", Notice("Перс", "Тихоня", "привет")));

    [Fact]
    public void ReplyToOtherBotMessageExplains()
        => Assert.IsType<ReplyRequest>(Parse("ага", "Подключены:\n🟢 Перс — PW, бот работает"));

    [Fact]
    public void WhisperCommandTakesCharacterNickAndRest()
        => Assert.Equal(new WhisperRequest("Перс", "Тихоня", "как дела?"), Parse("/w Перс  Тихоня как   дела?"));

    [Fact]
    public void WhisperCommandWithoutTextShowsHow()
        => Assert.Contains("/w Персонаж Ник текст", ((ReplyRequest)Parse("/w Перс Тихоня")).Html);

    [Fact]
    public void TooLongTextIsRefusedWithReason()
    {
        var reply = (ReplyRequest)Parse("/w Перс Тихоня " + new string('а', GameCaller.MaxChatText + 1));

        Assert.Contains($"длиннее {GameCaller.MaxChatText}", reply.Html);
    }

    [Fact]
    public void TooLongNickIsRefused()
        => Assert.IsType<ReplyRequest>(Parse($"/w Перс {new string('я', GameCaller.MaxChatNick + 1)} привет"));

    [Fact]
    public void WhisperResultNamesWhoAndWhom()
    {
        var w = new WhisperRequest("Перс", "Тихоня", "<3");

        Assert.Equal("✅ <code>Перс</code> → <code>Тихоня</code>: &lt;3", TelegramCommands.Whispered(w, null));
        Assert.StartsWith("❌ Не отправлено", TelegramCommands.Whispered(w, "персонаж не в мире"));
    }

    [Fact]
    public void PlainTextGetsHelp() => Assert.Equal(TelegramCommands.Escape(TelegramCommands.Help), ((ReplyRequest)Parse("привет")).Html);
}

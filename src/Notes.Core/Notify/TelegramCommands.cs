using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Notes.Core.Calls;

namespace Notes.Core.Notify;

/// <summary>Подключённая вкладка — для ответов в Telegram: ник, сервер (название профиля), работает ли бот.</summary>
public sealed record TelegramCharacter(string Nick, string Server, bool Running);

/// <summary>Что просят командой из Telegram.</summary>
public abstract record TelegramRequest;

/// <summary>/list — кто подключён.</summary>
public sealed record ListRequest : TelegramRequest;

/// <summary>Шепнуть: от нашего персонажа <paramref name="From"/> игроку <paramref name="To"/>.</summary>
public sealed record WhisperRequest(string From, string To, string Text) : TelegramRequest;

/// <summary>Ответить сразу этим текстом (подсказка, почему нельзя) — в разметке HTML.</summary>
public sealed record ReplyRequest(string Html) : TelegramRequest;

/// <summary>
/// Команды из Telegram: что просят (<see cref="Parse"/>) и тексты ответов — в разметке HTML (parse_mode=HTML). Ники — в
/// &lt;code&gt;: в Telegram такой текст копируется нажатием.
/// </summary>
public static class TelegramCommands
{
    public const string Help =
        "Команды:\n"
        + "• ответьте на уведомление о сообщении — ваш персонаж шепнёт ответ тому, кто написал\n"
        + "• /w Персонаж Ник текст — шепнуть от своего персонажа\n"
        + "• /list — какие персонажи подключены (ник копируется нажатием)";

    /// <summary>Что просят сообщением.</summary>
    public static TelegramRequest Parse(TelegramMessage message)
    {
        var text = message.Text.Trim();

        // Ответ на сообщение бота: на уведомление о сообщении — шёпот тому, кто написал
        if (message.ReplyToText is { } replied && !text.StartsWith("/", StringComparison.Ordinal))
        {
            return ChatNotifier.ParseNotice(replied) is { } notice
                ? Whisper(notice.Host, notice.From, text)
                : new ReplyRequest("Ответить шёпотом можно только на уведомление о сообщении в игре");
        }

        if (!text.StartsWith("/", StringComparison.Ordinal))
            return new ReplyRequest(Escape(Help));

        var parts = text.Split([' ', '\n', '\t'], 2);
        // «/list@ИмяБота» — так команда приходит, если выбрать её из меню в группе
        var command = parts[0];
        var at = command.IndexOf('@');
        if (at > 0)
            command = command.Substring(0, at);
        var rest = parts.Length > 1 ? parts[1].Trim() : "";

        switch (command.ToLowerInvariant())
        {
            case "/list":
                return new ListRequest();
            case "/w":
                var words = rest.Split([' ', '\n', '\t'], 3, StringSplitOptions.RemoveEmptyEntries);
                return words.Length < 3
                    ? new ReplyRequest("Так: /w Персонаж Ник текст — от вашего персонажа (/list) игроку")
                    : Whisper(words[0], words[1], words[2]);
            default:
                return new ReplyRequest(Escape(Help));
        }
    }

    // Шёпот — одной строкой (в игре чат без переносов); длины — как берёт игра
    private static TelegramRequest Whisper(string from, string to, string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length == 0)
            return new ReplyRequest("Пустой текст — нечего шептать");
        if (to.Length > GameCaller.MaxChatNick)
            return new ReplyRequest($"Ник длиннее {GameCaller.MaxChatNick} знаков — в игре таких нет");
        if (text.Length > GameCaller.MaxChatText)
            return new ReplyRequest($"Текст длиннее {GameCaller.MaxChatText} знаков ({text.Length}) — игра столько не пропустит, сократите");
        return new WhisperRequest(from, to, text);
    }

    /// <summary>Чем кончился шёпот: null — отправлено, иначе причина.</summary>
    public static string Whispered(WhisperRequest w, string? problem)
        => problem is null
            ? $"✅ {Code(w.From)} → {Code(w.To)}: {Escape(w.Text)}"
            : $"❌ Не отправлено ({Code(w.From)} → {Code(w.To)}): {Escape(problem)}";

    /// <summary>Нашего персонажа с таким ником нет среди подключённых.</summary>
    public static string NotConnected(string nick) => $"{Code(nick)} не подключён — кто подключён: /list";

    /// <summary>Список подключённых: «🟢 <code>Ник</code> — Comeback 1.4.6, бот работает».</summary>
    public static string List(IReadOnlyList<TelegramCharacter> connected)
    {
        if (connected.Count == 0)
            return "Подключённых персонажей нет";

        var text = new StringBuilder("Подключены:");
        foreach (var c in connected.OrderBy(c => c.Server, StringComparer.CurrentCulture).ThenBy(c => c.Nick, StringComparer.CurrentCulture))
            text.Append($"\n{(c.Running ? "🟢" : "🔴")} {Code(c.Nick)} — {Escape(c.Server)}, {(c.Running ? "бот работает" : "бот стоит")}");
        return text.ToString();
    }

    /// <summary>Текст, который копируется нажатием.</summary>
    public static string Code(string text) => $"<code>{Escape(text)}</code>";

    /// <summary>Текст разметки без тегов — для лога окна.</summary>
    public static string Plain(string html)
        => Regex.Replace(html, "<[^>]*>", "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    /// <summary>Для разметки HTML Telegram: &lt;, &gt; и &amp; — сущностями.</summary>
    public static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

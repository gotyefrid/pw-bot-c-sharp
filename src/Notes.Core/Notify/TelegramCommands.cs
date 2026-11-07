using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Notes.Core.Notify;

/// <summary>Подключённая вкладка — для ответов в Telegram: ник, сервер (название профиля), работает ли бот.</summary>
public sealed record TelegramCharacter(string Nick, string Server, bool Running);

/// <summary>
/// Ответы на команды из Telegram — текст в разметке HTML (parse_mode=HTML). Ники — в &lt;code&gt;: в Telegram такой текст
/// копируется нажатием.
/// </summary>
public static class TelegramCommands
{
    public const string Help =
        "Команды:\n/list — какие персонажи подключены (ник копируется нажатием)";

    /// <summary>Ответ на сообщение; null — отвечать нечего.</summary>
    public static string? Answer(TelegramMessage message, IReadOnlyList<TelegramCharacter> connected)
    {
        var text = message.Text.Trim();
        if (!text.StartsWith("/", StringComparison.Ordinal))
            return Escape(Help);

        // «/list@ИмяБота» — так команда приходит, если выбрать её из меню в группе
        var command = text.Split([' ', '\n'], 2)[0];
        var at = command.IndexOf('@');
        if (at > 0)
            command = command.Substring(0, at);

        return command.ToLowerInvariant() switch
        {
            "/list" => List(connected),
            _ => Escape(Help),
        };
    }

    /// <summary>Список подключённых: «● <code>Ник</code> — Comeback 1.4.6, бот работает».</summary>
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
        => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", "").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

    /// <summary>Для разметки HTML Telegram: &lt;, &gt; и &amp; — сущностями.</summary>
    public static string Escape(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}

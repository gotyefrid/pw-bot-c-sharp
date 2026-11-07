using System;
using System.Linq;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Notify;

/// <summary>
/// Пока бот запущен (<see cref="Active"/>), следит за чатом в каждом снимке и о новых сообщениях выбранных каналов пишет в
/// Telegram (<paramref name="send"/>). История до запуска не пересылается; свои сообщения и системные (у них нет отправителя) — тоже.
/// Канал, номер которого в профиле не проверен (пусто), не пересылается, даже если включён в окне.
/// </summary>
/// <param name="channels">Номера каналов из профиля сервера.</param>
/// <param name="settings">Текущие настройки уведомлений (читаются в каждом снимке: правка в окне сразу действует).</param>
/// <param name="send">Куда отдать готовый текст — разметка HTML, <see cref="Format"/> (в фоне: снимки не ждут сети).</param>
public sealed class ChatNotifier(ChatChannels channels, Func<ChatNotifySettings> settings, Action<string> send)
{
    private volatile bool _active;
    private volatile bool _skipHistory = true;
    private int? _last;

    /// <summary>Бот запущен. Включили — всё, что уже есть в чате, считается прочитанным.</summary>
    public bool Active
    {
        get => _active;
        set
        {
            if (value && !_active)
                _skipHistory = true;
            _active = value;
        }
    }

    /// <summary>Новый снимок мира (поток снимков).</summary>
    public void Observe(WorldState world)
    {
        if (world.Chat.Count == 0)
            return;

        var newest = world.Chat.Max(m => m.Number);
        // Номера начались заново (перезашли в игру) — тоже история
        if (!_active || _skipHistory || _last is not { } last || newest < last)
        {
            _skipHistory = false;
            _last = newest;
            return;
        }

        var s = settings();
        if (s.CanSend)
        {
            foreach (var message in world.Chat.Where(m => m.Number > last).OrderBy(m => m.Number))
            {
                if (Kind(message, world, s) is { } kind)
                {
                    var (from, text) = Split(message.Plain);
                    send(Format(world.Host.Name, kind, from, text));
                }
            }
        }

        _last = newest;
    }

    /// <summary>
    /// Уведомление в разметке HTML Telegram: «<code>Перс</code> — лично от <code>Ник</code>:\nтекст». Ники — в &lt;code&gt;:
    /// копируются нажатием. Без разметки (так Telegram отдаёт текст сообщения, на которое ответили) — «Перс — лично от Ник:\nтекст»:
    /// по нему ответ находит, кто кому писал (<see cref="ParseNotice"/>).
    /// </summary>
    public static string Format(string host, string kind, string? from, string text)
        => from is null
            ? $"{TelegramCommands.Code(host)} — {kind}:\n{TelegramCommands.Escape(text)}"
            : $"{TelegramCommands.Code(host)} — {kind} от {TelegramCommands.Code(from)}:\n{TelegramCommands.Escape(text)}";

    /// <summary>
    /// Из текста уведомления (каким его отдаёт Telegram — без разметки): кому писали (наш персонаж), в какой канал и кто.
    /// null — это не уведомление о сообщении с отправителем.
    /// </summary>
    public static (string Host, string Kind, string From)? ParseNotice(string notice)
    {
        var line = notice.Split('\n')[0];
        var dash = line.IndexOf(" — ", StringComparison.Ordinal);
        var by = line.IndexOf(" от ", dash < 0 ? 0 : dash, StringComparison.Ordinal);
        if (dash <= 0 || by < 0 || !line.EndsWith(":", StringComparison.Ordinal))
            return null;
        var host = line.Substring(0, dash);
        var kind = line.Substring(dash + 3, by - dash - 3);
        var from = line.Substring(by + 4, line.Length - by - 5);
        return from.Length == 0 || kind.Length == 0 ? null : (host, kind, from);
    }

    /// <summary>
    /// Кто написал и что: игра пишет «Ник: текст», в личке — «Ник шепчет: текст». Ник — до первого «: »; не нашли —
    /// отправитель null, текст целиком.
    /// </summary>
    public static (string? From, string Text) Split(string plain)
    {
        var colon = plain.IndexOf(": ", StringComparison.Ordinal);
        if (colon <= 0)
            return (null, plain);
        var from = plain.Substring(0, colon).Trim();
        const string whispers = " шепчет";
        if (from.EndsWith(whispers, StringComparison.Ordinal))
            from = from.Substring(0, from.Length - whispers.Length).Trim();
        return from.Length == 0 ? (null, plain) : (from, plain.Substring(colon + 2).Trim());
    }

    // Название канала, если о нём писать; null — не писать
    private string? Kind(ChatMessage m, WorldState w, ChatNotifySettings s)
    {
        // Без отправителя — системные и свои («Вы говорите с …»); свои в группе, клане и общем — с WID персонажа
        if (m.SenderWid == uint.MaxValue || m.SenderWid == w.Host.Wid)
            return null;
        if (s.Private && m.Channel == channels.Private)
            return "лично";
        if (s.Group && m.Channel == channels.Group)
            return "группа";
        if (s.Clan && m.Channel == channels.Clan)
            return "клан";
        if (s.Common && m.Channel == channels.Common)
            return "общий";
        return null;
    }
}

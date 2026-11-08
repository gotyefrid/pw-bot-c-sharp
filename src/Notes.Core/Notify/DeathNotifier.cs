using System;
using Notes.Core.Settings;
using Notes.Core.Stats;

namespace Notes.Core.Notify;

/// <summary>
/// Персонаж погиб, пока работал бот (не в сливе опыта: так же считает статистика, <see cref="StatKind.Death"/>) — пишет в
/// Telegram. Стоит между мозгом и статистикой: события отдаёт дальше (<paramref name="inner"/>; нет — статистику не ведём).
/// </summary>
/// <param name="settings">Текущие настройки уведомлений (читаются при каждой смерти: правка в окне сразу действует).</param>
/// <param name="host">Ник персонажа; не известен — «Персонаж».</param>
/// <param name="revives">Бот встанет в городе и вернётся (иначе остановится).</param>
/// <param name="send">Куда отдать готовый текст — разметка HTML (зовётся из потока снимков: сеть — в фоне).</param>
public sealed class DeathNotifier(IStatsSink? inner, Func<ChatNotifySettings> settings, Func<string?> host, Func<bool> revives,
    Action<string> send) : IStatsSink
{
    public void Add(StatEvent stat)
    {
        inner?.Add(stat);
        if (stat.Kind != StatKind.Death)
            return;
        var s = settings();
        if (s.CanSend && s.Death)
            send(Format(host() ?? "Персонаж", revives()));
    }

    /// <summary>
    /// После смерти бот встанет в городе и вернётся: включено «вставать после смерти», и режим это умеет (фарм мобов, сбор
    /// ресурсов); иначе — остановится.
    /// </summary>
    public static bool Revives(BotSettings s)
        => s.Target.ReviveAndReturn && s.Mode is BotMode.FarmMobs or BotMode.GatherResources;

    /// <summary>«<code>Ник</code> — погиб, встану в городе и вернусь» (или «бот остановлен»).</summary>
    public static string Format(string host, bool revives)
        => $"{TelegramCommands.Code(host)} — погиб, {(revives ? "встану в городе и вернусь" : "бот остановлен")}";
}

using System;
using System.Collections.Generic;
using Notes.Core.Notify;
using Notes.Core.Settings;
using Notes.Core.Stats;
using Xunit;

namespace Notes.Tests.Notify;

/// <summary>Уведомление в Telegram о смерти персонажа: о какой писать, что писать, статистика не теряется.</summary>
public class DeathNotifierTests
{
    private readonly List<string> _sent = [];
    private readonly List<StatEvent> _stats = [];
    private readonly ChatNotifySettings _settings = new() { Token = "123:abc", ChatId = "42" };
    private bool _revives = true;

    private sealed class ListSink(List<StatEvent> events) : IStatsSink
    {
        public void Add(StatEvent stat) => events.Add(stat);
    }

    private DeathNotifier Notifier(bool withStats = true)
        => new(withStats ? new ListSink(_stats) : null, () => _settings, () => "Ник", () => _revives, _sent.Add);

    private static StatEvent Event(StatKind kind) => new(DateTime.UtcNow, kind);

    [Fact]
    public void DeathIsSentAndStillCounted()
    {
        var notifier = Notifier();
        notifier.Add(Event(StatKind.Kill));
        notifier.Add(Event(StatKind.Death));

        Assert.Equal(["<code>Ник</code> — погиб, встану в городе и вернусь"], _sent);
        Assert.Equal(2, _stats.Count);
    }

    [Fact]
    public void WithoutReviveSaysBotStopped()
    {
        _revives = false;
        Notifier(withStats: false).Add(Event(StatKind.Death));

        Assert.Equal(["<code>Ник</code> — погиб, бот остановлен"], _sent);
    }

    [Fact]
    public void SwitchedOffOrNoTokenSendsNothing()
    {
        _settings.Death = false;
        Notifier().Add(Event(StatKind.Death));
        _settings.Death = true;
        _settings.Token = "";
        Notifier().Add(Event(StatKind.Death));

        Assert.Empty(_sent);
        Assert.Equal(2, _stats.Count);
    }

    // Смерть в сливе опыта статистика не считает (StatsWatcher) — значит, и уведомления о ней нет
    [Fact]
    public void DrainEventsAreNotDeaths()
    {
        var notifier = Notifier();
        notifier.Add(Event(StatKind.DrainStart));
        notifier.Add(Event(StatKind.DrainEnd));

        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData(BotMode.FarmMobs, true, true)]
    [InlineData(BotMode.GatherResources, true, true)]
    [InlineData(BotMode.FarmMobs, false, false)]
    [InlineData(BotMode.LevelUp, true, false)]
    [InlineData(BotMode.Clicker, true, false)]
    public void RevivesOnlyInModesThatCan(BotMode mode, bool setting, bool expected)
    {
        var s = new BotSettings { Mode = mode };
        s.Target.ReviveAndReturn = setting;
        Assert.Equal(expected, DeathNotifier.Revives(s));
    }
}

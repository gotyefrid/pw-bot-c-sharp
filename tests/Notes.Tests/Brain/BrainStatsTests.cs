using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.Stats;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>Статистика из хода мозга: что видно в бою и луте, попадает в приёмник; поведения о ней не знают.</summary>
public class BrainStatsTests
{
    private static readonly ClassSkills Skills = new() { DefaultAttack = 299 };

    private readonly FakeWorld _world = new() { BagSlots = 32, Money = 1000 };
    private readonly FakeActions _actions = new();
    private readonly BotSettings _settings = new();
    private readonly Sink _stats = new();
    private IBotRunner? _brain;

    private sealed class Sink : IStatsSink
    {
        public List<StatEvent> Events { get; } = [];
        public void Add(StatEvent stat) => Events.Add(stat);
    }

    private IBotRunner Brain => _brain ??= BotModes.Create(BotMode.FarmMobs, new ActionRunner(new GameControl(_actions), NullLogger.Instance),
        Skills, _settings, NullLogger.Instance, random: new Random(1), stats: _stats);

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    public BrainStatsTests()
    {
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Target.TravelFrom = 0;
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Loot.Enabled = true;
    }

    [Fact]
    public void KillAndPickedMoneyAreRecordedSessionEndsOnReset()
    {
        var wolf = _world.AddMob(0x80000001, "Волк", 2, hp: 100);
        _world.TargetWid = wolf.Wid;
        Tick();
        _world.Replace(wolf, m => m with { State = NpcState.Dead });
        _world.TargetWid = 0;
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 3044, GroundItemKind.Money, new Position(3, 0, 0), "Монета"));
        Tick();
        Assert.Contains("pickup-approach C0000001", _actions.Calls);

        _world.Ground.Clear();
        _world.Money = 1022;
        Tick();
        Brain.Reset();

        Assert.Equal([StatKind.SessionStart, StatKind.Kill, StatKind.Money, StatKind.SessionEnd], _stats.Events.Select(e => e.Kind));
        Assert.Equal(22, _stats.Events.Single(e => e.Kind == StatKind.Money).Amount);
    }

    [Fact]
    public void MoneyFromElsewhereIsNotLoot()
    {
        Tick();
        _world.Money = 5000;
        Tick();

        Assert.DoesNotContain(_stats.Events, e => e.Kind == StatKind.Money);
    }
}

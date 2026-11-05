using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>Лог нападений: кто напал, что решил бой и почему, за сколько перешёл на него, когда отстал.</summary>
public class AttackWatchTests
{
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    private IBotRunner Brain => _brain ??= BotModes.Create(BotMode.FarmMobs,
        new ActionRunner(new GameControl(_actions), NullLogger.Instance), Skills, _settings,
        new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), [], random: new Random(1));

    public AttackWatchTests()
    {
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
    }

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    private IEnumerable<string> Lines => _log.Select(e => e.Message);

    private string Line(string start) => Assert.Single(Lines, l => l.StartsWith(start));

    /// <summary>Бой с волком: сами выбрали его, бой начался.</summary>
    private NpcInfo FightWolf(uint targetWid = 0)
    {
        var wolf = _world.AddMob(0x80000001, "Волк", 3, targetWid);
        _world.TargetWid = wolf.Wid;
        Tick();
        Assert.Contains(Lines, l => l.StartsWith("Бой: Волк"));
        return wolf;
    }

    [Fact]
    public void AttackerIsLoggedWithWhoWhomAndCurrentFight()
    {
        FightWolf(FakeWorld.HostWid);
        _world.AddMob(0x80000002, "Медведь", 12, FakeWorld.HostWid, hp: 900);
        Tick();

        Assert.Equal("Нападение: Медведь 0x80000002 → на меня (его цель — мы), бьёт, 12,0 м, HP 900; сейчас бой: Волк", Line("Нападение"));
        // Волк тоже бьёт меня и ближе
        Assert.Equal("Медведь: сначала добиваю Волк: он ближе", Line("Медведь:"));
    }

    [Fact]
    public void OwnFightTargetHittingBackIsNotAnAttack()
    {
        var wolf = FightWolf();
        _world.Replace(wolf, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        Tick();

        Assert.DoesNotContain(Lines, l => l.StartsWith("Нападение") || l.StartsWith("Волк:"));
    }

    [Fact]
    public void SwitchIsLoggedWithReactionTime()
    {
        // Волк нас не бьёт — напал медведь: бой переходит на него
        FightWolf();
        var bear = _world.AddMob(0x80000002, "Медведь", 5, FakeWorld.HostWid);
        _world.Attacks.Add(new AttackEvent(1, 0, bear.Wid, 0, [new AttackHit(FakeWorld.HostWid, 120, 0)]));
        Tick();
        Assert.Contains("Медведь: беру в цель", Lines);
        Assert.Equal("select 80000002", _actions.Calls.Last());

        _world.TargetWid = bear.Wid;
        Tick(0.5);

        Assert.Equal("Медведь: бой с ним — через 0,5 с после нападения, 0,5 с после первого удара", Line("Медведь: бой с ним"));
    }

    [Fact]
    public void CloserAttackerWaitsForCurrentOneAsDangerous()
    {
        // Медведь ближе, но волк бьёт так же — бой не переключается
        FightWolf(FakeWorld.HostWid);
        _world.AddMob(0x80000002, "Медведь", 1, FakeWorld.HostWid);
        Tick();

        Assert.Equal("Медведь: Волк не менее опасен — сначала добиваю его", Line("Медведь:"));
    }

    [Fact]
    public void BusyTurnIsLogged()
    {
        // Пету плохо — ход у лечения, бой его не получил
        _settings.Pet.Enabled = true;
        _settings.Pet.HealPercent = 70;
        _world.SetPet(1, hpRatio: 0.4f);
        _world.AddSkill(330);
        _world.AddMob(0x80000002, "Медведь", 5, FakeWorld.HostWid);
        Tick();

        Assert.Equal("Медведь: ход занят: пет — лечу пета", Line("Медведь:"));
    }

    [Fact]
    public void AttackerIgnoredWhenRuleIsOff()
    {
        _settings.Target.PreferAggressive = false;
        FightWolf();
        _world.AddMob(0x80000002, "Медведь", 5, FakeWorld.HostWid);
        Tick();

        Assert.Equal("Медведь: «сначала тех, кто бьёт» выключено — не отвлекаюсь", Line("Медведь:"));
    }

    [Fact]
    public void CloserAttackerGoesFirst()
    {
        _world.AddMob(0x80000001, "Волк", 3, FakeWorld.HostWid);
        _world.AddMob(0x80000002, "Медведь", 10, FakeWorld.HostWid);
        Tick();

        Assert.Equal("Волк: беру в цель", Line("Волк:"));
        Assert.Equal("Медведь: сначала Волк: он ближе", Line("Медведь:"));
    }

    [Fact]
    public void StandingWithOurTargetIsAnAttack()
    {
        // Моб с нашей целью стоит между ударами — всё равно напал (застрявшую после отагра цель снимает NpcTracker)
        _world.AddMob(0x80000002, "Медведь", 5, FakeWorld.HostWid, NpcState.Standing);
        Tick();

        Assert.StartsWith("Нападение: Медведь 0x80000002 → на меня (его цель — мы), стоит", Line("Нападение"));
    }

    [Fact]
    public void ChangesAreLoggedOnce()
    {
        FightWolf(FakeWorld.HostWid);
        _world.AddMob(0x80000002, "Медведь", 12, FakeWorld.HostWid);
        for (var i = 0; i < 5; i++)
            Tick();

        Assert.Single(Lines, l => l.StartsWith("Нападение"));
        Assert.Single(Lines, l => l.StartsWith("Медведь:"));
    }

    [Theory]
    [InlineData("dead", "Медведь: убит")]
    [InlineData("target", "Медведь: отстал: не бьёт нас 10 с")]
    [InlineData("other", "Медведь: отстал: сменил цель")]
    [InlineData("returning", "Медведь: бросил нас, уходит на место")]
    [InlineData("gone", "Медведь: пропал из виду")]
    public void LeavingIsLogged(string how, string line)
    {
        FightWolf(FakeWorld.HostWid);
        var bear = _world.AddMob(0x80000002, "Медведь", 12, FakeWorld.HostWid);
        Tick();

        if (how == "gone")
            _world.Npcs.Remove(_world.Npcs.Single(n => n.Wid == bear.Wid));
        else
            _world.Replace(bear, m => how switch
            {
                "dead" => m with { State = NpcState.Dead },
                "target" => m with { TargetWid = 0 },
                "other" => m with { TargetWid = 0x0004_1234 },
                _ => m with { Returning = true },
            });
        Tick();

        Assert.Contains(line, Lines);
    }
}

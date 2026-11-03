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

/// <summary>Умер не от слива опыта: «встать в городе и вернуться» — или стоп, как раньше.</summary>
public class AccidentalDeathTests
{
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };
    private static readonly Position Town = new(300, 0, 0);

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;
    private string? _stop;
    private int _seen;
    private int _flown;
    private int _walked;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public AccidentalDeathTests()
    {
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Potions.UseHp = false;
        _settings.Target.ReviveAndReturn = true;
        _settings.Target.TravelHeight = 30;
        _settings.Target.FarmPoints = [FarmPoint.At("Поляна", new Position(0, 0, 0))];
        _settings.Target.FarmCenter = "Поляна";
    }

    private IBotRunner Brain
    {
        get
        {
            if (_brain is not null)
                return _brain;
            _brain = BotModes.Create(BotMode.FarmMobs, new ActionRunner(new GameControl(_actions), NullLogger.Instance), Skills, _settings,
                new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), random: new Random(1));
            _brain.StopRequested += reason => _stop = reason;
            return _brain;
        }
    }

    private void Tick()
    {
        Brain.Tick(_world.Wait(0.25).Snapshot());
        foreach (var call in _actions.Calls.Skip(_seen).ToList())
        {
            switch (call.Split(' ')[0])
            {
                case "fly-toggle":
                    _world.Flying = _world.Flying != true;
                    if (_world.Flying == false)
                        _world.Position = _world.Position with { Height = 0 };
                    break;
                case "fly":
                    _world.Position = _actions.FlyPoints[_flown++];
                    break;
                case "move":
                    _world.Position = _actions.MovePoints[_walked++];
                    break;
                case "revive-town":
                    _world.Hp = _world.MaxHp;
                    _world.Position = Town;
                    break;
            }
        }
        _seen = _actions.Calls.Count;
    }

    private void TickFor(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.25)
            Tick();
    }

    private void Die()
    {
        _world.Hp = 0;
        TickFor(15);
    }

    [Fact]
    public void RevivesAndFliesBackToFarmAtTravelHeight()
    {
        TickFor(1);

        Die();

        Assert.Contains("revive-town", _actions.Calls);
        Assert.Contains(new Position(300, 300, 0), _actions.FlyPoints);
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= FlightTrip.Arrived);
        Assert.False(_world.Flying);
        Assert.Null(_stop);
    }

    [Fact]
    public void OffStopsAsBefore()
    {
        _settings.Target.ReviveAndReturn = false;
        TickFor(1);

        _world.Hp = 0;
        Tick();

        Assert.Equal("персонаж погиб", _stop);
        Assert.DoesNotContain("revive-town", _actions.Calls);
    }

    private bool FliesWithin(double minutes)
    {
        var flown = _actions.FlyPoints.Count;
        TickFor(minutes * 60);
        return _actions.FlyPoints.Count > flown;
    }

    // Встали в городе (первый живой снимок после «Ближнего города»)
    private void DieAndRevive()
    {
        _world.Hp = 0;
        TickFor(3);
    }

    [Fact]
    public void KilledAgainSoonWaitsInTownLongerEachTime()
    {
        TickFor(1);
        Die();
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= FlightTrip.Arrived);

        // Убили через 3 мин — стоим 5 мин
        TickFor(3 * 60);
        DieAndRevive();
        Assert.False(FliesWithin(4.5));
        Assert.True(FliesWithin(1));
        TickFor(15);
        Assert.Contains(_log, e => e.Message.Contains("постою в городе 5 мин"));

        // И снова — 10 мин
        TickFor(60);
        DieAndRevive();
        Assert.False(FliesWithin(9.5));
        Assert.True(FliesWithin(1));
        Assert.Null(_stop);
    }

    [Fact]
    public void TenQuietMinutesResetPause()
    {
        TickFor(1);
        Die();
        TickFor(60);
        DieAndRevive();
        TickFor(6 * 60); // 5 мин в городе, долетели

        TickFor(10 * 60);
        DieAndRevive();

        Assert.True(FliesWithin(0.5));
    }

    [Fact]
    public void NoFlyerWalksBack()
    {
        _world.HasFlyer = false;
        TickFor(1);

        Die();

        Assert.Empty(_actions.FlyPoints);
        Assert.DoesNotContain("fly-toggle", _actions.Calls);
        Assert.Equal(new Position(0, 0, 0), _world.Position);
    }
}

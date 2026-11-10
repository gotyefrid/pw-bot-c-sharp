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

/// <summary>Режим «Собирать ресурсы», перс умер: «встать в городе и вернуться» к точке маршрута — или стоп, как раньше.</summary>
public class RouteDeathTests
{
    private const uint Pickaxe = 3073;
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };
    private static readonly Position Town = new(1000, 0, 0);

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

    public RouteDeathTests()
    {
        _settings.Mode = BotMode.GatherResources;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Potions.UseHp = false;
        _settings.Target.ReviveAndReturn = true;
        _settings.Target.TravelHeight = 30;
        _world.Bag.Add(new InventoryItem(5, Pickaxe, 0, 1, null, null));
    }

    private IBotRunner Brain
    {
        get
        {
            if (_brain is not null)
                return _brain;
            _brain = BotModes.Create(BotMode.GatherResources, new ActionRunner(new GameControl(_actions), NullLogger.Instance), Skills, _settings,
                new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), [Pickaxe], 0, new Random(1));
            _brain.StopRequested += reason => _stop = reason;
            return _brain;
        }
    }

    // Мир отвечает на команды сразу: полёт и бег — уже там, «Полёт» — в воздухе или на земле, «Ближний город» — живой в городе
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

    // Обход в полёте: две точки на 30 м, перс у первой
    private void FlyingRoute()
    {
        _settings.Route.Points = [RoutePoint.At("1", new Position(0, 30, 0), flying: true), RoutePoint.At("2", new Position(200, 30, 0), flying: true)];
        _world.Flying = true;
        _world.Position = new Position(0, 30, 0);
    }

    [Fact]
    public void RevivesAndFliesBackToPointWhereDiedThenGoesOn()
    {
        FlyingRoute();
        TickFor(1);
        // Долетели до 2-й — там убили
        _world.Position = new Position(200, 30, 0);
        TickFor(1);

        _world.Hp = 0;
        TickFor(40);

        Assert.Contains("revive-town", _actions.Calls);
        // Из города — вверх на высоту полёта (30 → 300 в памяти), к точке, где погибли, и зависнуть на её высоте
        Assert.Contains(new Position(1000, 300, 0), _actions.FlyPoints);
        Assert.Equal(new Position(200, 30, 0), _world.Position);
        Assert.True(_world.Flying);
        Assert.Contains(_log, e => e.Message == "Вернулся на место");
        Assert.Null(_stop);

        // У точки копать нечего — обход дальше, к 1-й
        var flown = _actions.FlyPoints.Count;
        TickFor(3);
        Assert.Contains(new Position(0, 30, 0), _actions.FlyPoints.Skip(flown));
    }

    [Fact]
    public void WithoutReviveSettingBotStops()
    {
        _settings.Target.ReviveAndReturn = false;
        FlyingRoute();
        TickFor(1);

        _world.Hp = 0;
        TickFor(5);

        Assert.DoesNotContain("revive-town", _actions.Calls);
        Assert.Equal("персонаж погиб", _stop);
    }
}

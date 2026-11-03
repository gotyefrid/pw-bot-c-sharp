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

/// <summary>«Старт» вдали от центра фарма: долететь до него, потом фармить.</summary>
public class StartTripBehaviorTests
{
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;
    private int _seen;
    private int _flown;
    private int _walked;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public StartTripBehaviorTests()
    {
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Potions.UseHp = false;
        _settings.Target.TravelFrom = 100;
        _settings.Target.TravelHeight = 23;
    }

    private void FarmAt(Position p, bool flying = false)
    {
        _settings.Target.FarmPoints = [FarmPoint.At("Поляна", p, flying)];
        _settings.Target.FarmCenter = "Поляна";
    }

    private IBotRunner Brain => _brain ??= BotModes.Create(BotMode.FarmMobs, new ActionRunner(new GameControl(_actions), NullLogger.Instance),
        Skills, _settings, new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), random: new Random(1));

    // Шаг мозга, потом ответ «игры» на то, что он отправил
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
            }
        }
        _seen = _actions.Calls.Count;
    }

    private void TickFor(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.25)
            Tick();
    }

    private bool Logged(string text) => _log.Any(e => e.Message.Contains(text));

    [Fact]
    public void FarStartTakesOffClimbsAndLandsAtCenter()
    {
        FarmAt(new Position(500, 0, 0));

        TickFor(10);

        Assert.Equal("fly-toggle", _actions.Calls[0]);
        Assert.Equal(new Position(0, 230, 0), _actions.FlyPoints[0]);
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(500, 0, 0)) <= FlightTrip.Arrived);
        Assert.False(_world.Flying);
        Assert.True(Logged("Долетел до центра фарма"));
    }

    [Fact]
    public void FlyingPointHoversAtItsHeight()
    {
        FarmAt(new Position(500, 300, 0), flying: true);

        TickFor(10);

        Assert.True(_world.Flying);
        Assert.Equal(new Position(500, 300, 0), _world.Position);
    }

    [Fact]
    public void WithoutFlightWalksSmart()
    {
        _actions.Capabilities = Capabilities.All.Without(Capability.FlyTo, "нет полёта в точку");
        FarmAt(new Position(500, 0, 0));

        TickFor(2);

        Assert.Contains(_actions.Calls, c => c.StartsWith("move") && c.EndsWith("умно"));
        Assert.Empty(_actions.FlyPoints);
        Assert.Equal(new Position(500, 0, 0), _world.Position);
    }

    [Theory]
    [InlineData(80, 100)]
    [InlineData(500, 0)]
    public void NearOrOffDoesNotFly(float x, int travelFrom)
    {
        _settings.Target.TravelFrom = travelFrom;
        FarmAt(new Position(x, 0, 0));

        TickFor(3);

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("fly"));
    }

    [Fact]
    public void StartPointCenterDoesNotFly()
    {
        TickFor(3);

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("fly"));
    }

    [Fact]
    public void TooFarWarnsAndStays()
    {
        FarmAt(new Position(5000, 0, 0));

        TickFor(3);

        Assert.Empty(_actions.Calls);
        Assert.True(Logged("слишком далеко"));
    }

    [Fact]
    public void OnlyOncePerStart()
    {
        FarmAt(new Position(500, 0, 0));
        TickFor(10);
        var calls = _actions.Calls.Count;

        _world.Position = new Position(0, 0, 0);
        TickFor(3);

        Assert.Equal(calls, _actions.Calls.Count);
    }

    // Слив опыта стоит раньше, но в воздухе на полпути не начинается
    [Fact]
    public void ExpDrainWaitsUntilArrived()
    {
        _settings.ExpDrain.Enabled = true;
        _settings.ExpDrain.MaxLevel = 10;
        _settings.ExpDrain.StartPercent = 80;
        _world.Level = 10;
        _world.Exp = 900;
        _world.ExpToLevel = 1000;
        FarmAt(new Position(500, 0, 0));

        Tick();

        Assert.Equal("fly-toggle", _actions.Calls[0]);
        Assert.False(Logged("сливаю"));
        TickFor(10);
        Assert.True(Logged("Долетел до центра фарма"));
        Assert.True(Logged("сливаю"));
    }

    // Было: слив ждал, пока перелёт решит, а бой тем временем брал моба — слив начинался только после боя
    [Fact]
    public void NearStartDrainsBeforeFight()
    {
        _settings.Target.KillMobs = true;
        _settings.ExpDrain.Enabled = true;
        _settings.ExpDrain.MaxLevel = 10;
        _settings.ExpDrain.StartPercent = 80;
        _world.Level = 10;
        _world.Exp = 900;
        _world.ExpToLevel = 1000;
        _world.AddMob(0x80000002, "Волк", 10);
        FarmAt(new Position(20, 0, 0));

        Tick();

        Assert.True(Logged("сливаю"));
        Assert.False(Logged("Бой:"));
    }
}

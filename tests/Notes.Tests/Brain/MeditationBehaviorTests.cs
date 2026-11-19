using System;
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

/// <summary>Медитация в режиме «Бить мобов»: когда садится, когда встаёт, что при нападении.</summary>
public class MeditationBehaviorTests
{
    private static readonly ClassSkills Skills = new() { DefaultAttack = 299 };

    private readonly FakeWorld _world = new() { Hp = 500, MaxHp = 500, Mp = 500, MaxMp = 500 };
    private readonly FakeActions _actions = new();
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;

    private IBotRunner Brain => _brain ??= BotModes.Create(BotMode.FarmMobs,
        new ActionRunner(new GameControl(_actions), NullLogger.Instance), Skills, _settings, NullLogger.Instance, random: new Random(1));

    public MeditationBehaviorTests()
    {
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.UseHp = false;
        _settings.Potions.UseMp = false;
        _settings.Target.TravelFrom = 0;
        var m = _settings.Meditation;
        m.Enabled = true;
        m.UseHp = true;
        m.HpPercent = 50;
        m.UseMp = true;
        m.MpPercent = 40;
        m.StandHpPercent = 90;
        m.StandMpPercent = 80;
    }

    private int _seen;
    private int _flown;
    private int _walked;

    // Шаг мозга, потом ответ «игры» на то, что он отправил: полёт, бег, сесть и встать
    private void Tick(double seconds = 0.25)
    {
        Brain.Tick(_world.Wait(seconds).Snapshot());
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
                case "sit":
                    _world.Sitting = true;
                    break;
                case "stand":
                    _world.Sitting = false;
                    break;
            }
        }
        _seen = _actions.Calls.Count;
    }

    private void TickUntil(Func<bool> done, int max = 200)
    {
        for (var i = 0; i < max && !done(); i++)
            Tick();
        Assert.True(done());
    }

    private int Count(string call) => _actions.Calls.Count(c => c == call);

    [Fact]
    public void FullHpAndMpDoesNotSit()
    {
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void LowHpSits()
    {
        _world.Hp = 200; // 40 %
        Tick();
        Assert.Equal(1, Count("sit"));
    }

    [Fact]
    public void BothRequiredWaitsForMpToo()
    {
        _settings.Meditation.RequireBoth = true;
        _world.Hp = 200;
        Tick();
        Assert.Equal(0, Count("sit"));

        _world.Mp = 100; // 20 %
        Tick();
        Assert.Equal(1, Count("sit"));
    }

    [Fact]
    public void AboveCompareSitsWhenHigher()
    {
        _settings.Meditation.UseHp = false;
        _settings.Meditation.MpCompare = MeditationCompare.AtLeast;
        _settings.Meditation.MpPercent = 30;
        _world.Mp = 200; // 40 % ≥ 30 %, но до «вставать при» 80 % не дошло
        Tick();
        Assert.Equal(1, Count("sit"));
    }

    [Fact]
    public void DisabledDoesNotSit()
    {
        _settings.Meditation.Enabled = false;
        _world.Hp = 100;
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void StandsWhenBothRestoredToOwnThresholds()
    {
        _world.Hp = 200;
        Tick();
        _world.Sitting = true;

        _world.Hp = 460; // 92 % ≥ 90 %, MP 100 % ≥ 80 %
        _world.Mp = 300; // 60 % < 80 % — ещё сидим
        Tick();
        Assert.Equal(0, Count("stand"));

        _world.Mp = 410; // 82 %
        Tick();
        Assert.Equal(1, Count("stand"));
    }

    [Fact]
    public void AttackedWhileSittingStands()
    {
        _world.Hp = 200;
        Tick();
        _world.Sitting = true;
        Tick();

        _world.AddMob(0x80000001, "Волк", 5, targetWid: FakeWorld.HostWid);
        Tick();
        Assert.Equal(1, Count("stand"));
    }

    [Fact]
    public void AttackedDoesNotSit()
    {
        _world.Hp = 200;
        _world.AddMob(0x80000001, "Волк", 5, targetWid: FakeWorld.HostWid);
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void FlyingDoesNotSit()
    {
        _world.Hp = 200;
        _world.Flying = true;
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void FlyingHereLandsThenSits()
    {
        _world.Flying = true;
        _world.Position = new Position(10, 100, 10);
        _world.Hp = 200;

        TickUntil(() => _world.Sitting == true);

        Assert.False(_world.Flying);
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(10, 0, 10)) <= FlightTrip.Arrived);
    }

    [Fact]
    public void FliesToPointThenSits()
    {
        _settings.Meditation.Points = [FarmPoint.At("Медитация 1", new Position(300, 0, 0))];
        _settings.Meditation.Point = "Медитация 1";
        _world.Hp = 200;

        TickUntil(() => _world.Sitting == true);

        Assert.True(_world.Position.HorizontalDistanceTo(new Position(300, 0, 0)) <= FlightTrip.Arrived);
        Assert.False(_world.Flying);
    }

    [Fact]
    public void AfterMeditationReturnsToAirFarm()
    {
        var farm = new Position(0, 300, 0);
        _settings.Target.FarmPoints = [FarmPoint.At("Поляна", farm, flying: true)];
        _settings.Target.FarmCenter = "Поляна";
        _settings.Meditation.Points = [FarmPoint.At("Медитация 1", new Position(200, 0, 0))];
        _settings.Meditation.Point = "Медитация 1";
        _world.Flying = true;
        _world.Position = farm;
        _world.Hp = 200;

        TickUntil(() => _world.Sitting == true);
        _world.Hp = 500;
        _world.Mp = 500;

        TickUntil(() => _world.Sitting == false && _world.Flying == true && _world.Position.DistanceTo(farm) <= FlightTrip.Arrived);
    }

    [Fact]
    public void AttackedOnTheWayDropsTrip()
    {
        _settings.Meditation.Points = [FarmPoint.At("Медитация 1", new Position(3000, 0, 0))];
        _settings.Meditation.Point = "Медитация 1";
        _world.Hp = 200;
        Tick();
        Tick();
        var flights = _actions.FlyPoints.Count;

        _world.AddMob(0x80000001, "Волк", 5, targetWid: FakeWorld.HostWid);
        Tick();
        Tick();

        Assert.Equal(0, Count("sit"));
        Assert.Equal(flights, _actions.FlyPoints.Count);
    }

    // SaudiKo 19.11: MP 12/1884, а бот бил моба за мобом и ни разу не сел
    [Fact]
    public void AfterKillSitsBeforeNextMob()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSkill = true;
        _settings.Meditation.UseHp = false;
        _world.AddSkill(299);
        var wolf = _world.AddMob(0x80000001, "Волк", 5);
        _world.TargetWid = wolf.Wid;
        _world.AddMob(0x80000002, "Волк", 20);
        Tick();
        Tick();
        Assert.Contains(_actions.Calls, c => c.StartsWith("apply 299"));
        _world.Mp = 10; // потратил в бою
        Tick();
        Assert.Equal(0, Count("sit"));
        _world.Replace(wolf, m => m with { Hp = 0, State = NpcState.Dead });
        _world.TargetWid = 0;
        for (var i = 0; i < 20 && Count("sit") == 0; i++)
            Tick();
        Assert.Equal(1, Count("sit"));
    }

    // Флаг каста после убийства снимается не сразу: бой не должен успеть взять следующего моба
    [Fact]
    public void WaitsForCastEndInsteadOfNextMob()
    {
        _settings.Target.KillMobs = true;
        _settings.Meditation.UseHp = false;
        _world.Mp = 10;
        _world.Casting = true;
        _world.AddMob(0x80000002, "Волк", 20);
        Tick();
        Tick();
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("select"));
        Assert.Equal(0, Count("sit"));

        _world.Casting = false;
        Tick();
        Assert.Equal(1, Count("sit"));
    }

    [Fact]
    public void StuckCastFlagDoesNotBlockForever()
    {
        _settings.Target.KillMobs = true;
        _settings.Meditation.UseHp = false;
        _world.Mp = 10;
        _world.Casting = true;
        _world.AddMob(0x80000002, "Волк", 20);
        for (var i = 0; i < 30; i++)
            Tick();
        Assert.Contains(_actions.Calls, c => c.StartsWith("select"));
    }

    [Fact]
    public void NoCapabilityDoesNotSit()
    {
        _actions.Capabilities = Capabilities.All.Without(Capability.Meditate, "нет");
        _world.Hp = 200;
        Tick();
        Assert.Equal(0, Count("sit"));
    }
}

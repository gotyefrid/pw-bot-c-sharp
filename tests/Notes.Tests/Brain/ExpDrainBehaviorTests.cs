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

/// <summary>Слив опыта в режиме «Бить мобов»: снимки подставного мира, который отвечает на вызовы как игра.</summary>
public class ExpDrainBehaviorTests
{
    private const int Arrow = 125;
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;
    private int _seen;
    private int _flown;
    private int _walked;
    private Position _town = new(300, 0, 0);
    private string? _stop;

    /// <summary>Игра «засчитывает» выстрел (скилл уходит на перезарядку).</summary>
    private bool _hits = true;

    /// <summary>«Ближний город» срабатывает.</summary>
    private bool _revives = true;

    // Кто-то посреди дела из нескольких шагов (банк)
    private sealed class Holder : IBehavior, IMultiStepBehavior
    {
        public string? InMiddle { get; set; }
        public string Name => "дело";
        public string? Status => null;
        public bool Tick(BrainContext context) => false;
        public void OnOutcome(BrainContext context, ActionOutcome outcome) { }
        public void Reset() { }
    }

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public ExpDrainBehaviorTests()
    {
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Potions.UseHp = false;
        _settings.Combat.UseSkill = true;
        _settings.Combat.AttackSkillId = Arrow;
        _settings.ExpDrain.Enabled = true;
        _settings.ExpDrain.MaxLevel = 10;
        _settings.ExpDrain.StartPercent = 80;
        _settings.ExpDrain.TargetPercent = 10;
        _settings.ExpDrain.Mobs = 2;
        _settings.ExpDrain.FlightHeight = 23;
        _world.Level = 10;
        _world.Exp = 900;
        _world.ExpToLevel = 1000;
        _world.AddSkill(Arrow);
    }

    private IBotRunner Brain
    {
        get
        {
            if (_brain is not null)
                return _brain;
            return UseBrain(BotModes.Create(BotMode.FarmMobs, Runner(), Skills, _settings, BrainLog(), random: new Random(1)));
        }
    }

    private ActionRunner Runner() => new(new GameControl(_actions), NullLogger.Instance);
    private ILogger BrainLog() => new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг");

    private IBotRunner UseBrain(IBotRunner brain)
    {
        _brain = brain;
        _brain.StopRequested += reason => _stop = reason;
        return _brain;
    }

    // Свой набор: первым — тот, кто бывает посреди дела, дальше — как у фарма
    private void UseBrainWith(Holder holder)
    {
        var context = new BrainContext(Runner(), Skills, BrainLog(), new Random(1)) { Settings = _settings.Clone() };
        var afterDeath = new DeathReturnBehavior();
        var combat = new CombatBehavior();
        UseBrain(new BotBrain(context, [holder, afterDeath, new ExpDrainBehavior(new DeathDrain(afterDeath), c => c.FarmSpot), combat], combat));
    }

    private IEnumerable<string> NewCalls => _actions.Calls.Skip(_seen);

    // Шаг мозга, потом ответ «игры» на то, что он отправил
    private void Tick(double seconds = 0.25)
    {
        Brain.Tick(_world.Wait(seconds).Snapshot());
        foreach (var call in _actions.Calls.Skip(_seen).ToList())
        {
            var parts = call.Split(' ');
            switch (parts[0])
            {
                case "select":
                    _world.TargetWid = Convert.ToUInt32(parts[1], 16);
                    _world.SetCooldown(Arrow, 0); // пока выбирали, прошлая перезарядка кончилась
                    break;
                case "unselect":
                    _world.TargetWid = 0;
                    break;
                case "apply" when _hits:
                    _world.SetCooldown(Arrow, 3000);
                    break;
                case "recall":
                    _world.SetPet(1, summoned: false);
                    break;
                case "fly-toggle":
                    _world.Flying = _world.Flying != true;
                    if (_world.Flying == false)
                        _world.Position = _world.Position with { Height = 0 };
                    break;
                case "fly":
                    _world.Position = _actions.FlyPoints[_flown++];
                    break;
                case "revive-town" when _revives:
                    _world.Hp = _world.MaxHp;
                    _world.Position = _town;
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

    private void AddGroundMobs()
    {
        _world.Npcs.Add(new NpcInfo(0x80000001, 0x80000001, NpcKind.Mob, NpcState.Standing, 0, new Position(5, 200, 0), "Змея", 0));
        _world.AddMob(0x80000002, "Волк", 10);
        _world.AddMob(0x80000003, "Медведь", 20);
        _world.AddMob(0x80000004, "Далёкий", 60);
    }

    // Собрать 2 мобов и встать ждать смерти
    private void PullTwo()
    {
        AddGroundMobs();
        TickFor(3);
        Assert.Contains("unselect", _actions.Calls);
    }

    [Fact]
    public void BelowThresholdDoesNothing()
    {
        _world.Exp = 790;
        AddGroundMobs();

        TickFor(2);

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void PullsGroundMobsOneShotEachThenWaitsForDeath()
    {
        PullTwo();

        Assert.Equal(["select 80000002", "apply 125 80000002", "select 80000003", "apply 125 80000003", "unselect"], _actions.Calls);
        Assert.Contains("жду смерти", Brain.Status);
    }

    [Fact]
    public void PetIsRecalledFirst()
    {
        _settings.Pet.Enabled = true;
        _world.SetPet(1);
        AddGroundMobs();

        TickFor(1);

        Assert.Equal("recall", _actions.Calls[0]);
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("summon"));
    }

    [Fact]
    public void InterruptedCastIsShotAgainThenNextMob()
    {
        _hits = false;
        AddGroundMobs();

        TickFor(16);

        Assert.Equal(3, _actions.Calls.Count(c => c == "apply 125 80000002"));
        Assert.Contains("select 80000003", _actions.Calls);
    }

    [Fact]
    public void DeathDuringDrainRevivesInTownAndFliesBackHigh()
    {
        PullTwo();
        _world.Hp = 0;
        _world.Exp = 700;

        TickFor(1.5);
        Assert.Null(_stop);
        Assert.DoesNotContain("revive-town", NewCalls);

        TickFor(1);
        Assert.Contains("revive-town", _actions.Calls);
        TickFor(3);
        Assert.Contains(_log, e => e.Message.Contains("смерть сняла 200 опыта"));

        TickFor(2);
        // Взлёт, подъём строго вверх над городом до 23 (230 в памяти), потом отрезками к месту
        Assert.Equal(new Position(300, 230, 0), _actions.FlyPoints[0]);
        Assert.Equal(new Position(150, 230, 0), _actions.FlyPoints[1]);

        TickFor(3);
        Assert.False(_world.Flying);
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= 5);
        Assert.True(Logged("на месте — собираю мобов"));
        Assert.Null(_stop);
    }

    [Fact]
    public void DrainEndsWhenExpBelowTarget()
    {
        PullTwo();
        _world.Hp = 0;
        _world.Exp = 50;

        TickFor(12);

        Assert.True(Logged("Слив опыта закончен"));
        Assert.Equal(1, _actions.Calls.Count(c => c == "unselect"));
        Assert.Null(Brain.Status is { } s && s.Contains("слив") ? s : null);
    }

    [Fact]
    public void DeathOutsideDrainStopsBot()
    {
        _world.Exp = 500;
        _world.Hp = 0;

        Tick();

        Assert.Equal("персонаж погиб", _stop);
    }

    [Fact]
    public void DeathsWithoutExpLossTurnDrainOff()
    {
        for (var death = 0; death < 2; death++)
        {
            if (death == 0)
                PullTwo();
            else
                TickFor(3);
            _world.Hp = 0;
            TickFor(12);
        }

        Assert.True(Logged("2 смерти подряд не сняли опыт"));
        Assert.True(Logged("Слив опыта закончен"));
        var calls = _actions.Calls.Count;
        TickFor(5);
        Assert.Equal(calls, _actions.Calls.Count);
    }

    [Fact]
    public void LevelAboveMaxOnlyWarns()
    {
        _world.Level = 11;
        AddGroundMobs();

        TickFor(1);

        Assert.Empty(_actions.Calls);
        Assert.True(Logged("поздно"));
    }

    [Fact]
    public void NoReviveOnServerOnlyWarns()
    {
        _actions.Capabilities = Capabilities.All.Without(Capability.ReviveInTown, "нет функции");
        AddGroundMobs();

        TickFor(1);

        Assert.Empty(_actions.Calls);
        Assert.True(Logged("на этом сервере не работает"));
    }

    [Fact]
    public void InAirTakesFlyingMobsAndDoesNotLand()
    {
        _world.Flying = true;
        _world.Position = new Position(0, 200, 0);
        AddGroundMobs();

        TickFor(3);
        Assert.Equal("select 80000001", _actions.Calls[0]);

        _world.Hp = 0;
        _world.Flying = false;
        _world.Exp = 700;
        TickFor(12);

        Assert.True(Logged("на месте — собираю мобов"));
        Assert.True(_world.Flying);
        Assert.Equal(new Position(0, 200, 0), _world.Position);
    }

    [Fact]
    public void NoMobsAroundAbandonsAndFarmsOn()
    {
        TickFor(61);

        Assert.True(Logged("Слив опыта брошен"));
        Assert.Null(_stop);
    }

    [Fact]
    public void DisabledWhileDeadStillFliesBackThenFarms()
    {
        PullTwo();
        _world.Hp = 0;
        _world.Exp = 700;
        Tick();
        _settings.ExpDrain.Enabled = false;
        Brain.UpdateSettings(_settings);

        TickFor(12);

        Assert.Null(_stop);
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= 5);
        Assert.True(Logged("Слив опыта выключен"));
    }

    [Fact]
    public void WithoutFlightWalksBack()
    {
        _actions.Capabilities = Capabilities.All.Without(Capability.FlyTo, "нет полёта в точку");
        PullTwo();
        _world.Hp = 0;
        _world.Exp = 700;

        TickFor(12);

        Assert.Contains(_actions.Calls, c => c.StartsWith("move") && c.EndsWith("умно"));
        Assert.Empty(_actions.FlyPoints);
        Assert.True(Logged("на месте — собираю мобов"));
        Assert.Null(_stop);
    }

    [Fact]
    public void FarPlaceStopsBot()
    {
        _town = new Position(5000, 0, 0);
        PullTwo();
        _world.Hp = 0;

        TickFor(8);

        Assert.Contains("не лечу", _stop);
        Assert.Empty(_actions.FlyPoints);
    }

    [Fact]
    public void ReviveThatDoesNotWorkStopsBot()
    {
        _revives = false;
        PullTwo();
        _world.Hp = 0;

        TickFor(40);

        Assert.Equal(DeathReturnBehavior.ReviveAttempts, _actions.Calls.Count(c => c == "revive-town"));
        Assert.Equal("персонаж погиб", _stop);
    }

    [Fact]
    public void NobodyHitsPullsAgain()
    {
        PullTwo();

        TickFor(11);

        Assert.True(Logged("никто не бьёт"));
        Assert.Equal(2, _actions.Calls.Count(c => c == "select 80000002"));
    }

    [Fact]
    public void TargetNotBelowStartOnlyWarns()
    {
        _settings.ExpDrain.TargetPercent = 85;
        AddGroundMobs();

        TickFor(1);

        Assert.Empty(_actions.Calls);
        Assert.True(Logged("не меньше"));
    }

    [Fact]
    public void WaitsWhileSomeoneIsInTheMiddle()
    {
        var holder = new Holder { InMiddle = "убираю в банк" };
        UseBrainWith(holder);
        AddGroundMobs();

        TickFor(2);
        Assert.Empty(_actions.Calls);

        holder.InMiddle = null;
        TickFor(1);
        Assert.Contains("select 80000002", _actions.Calls);
    }

    [Fact]
    public void AttackSkillIsUsedEvenWhenFightIsWithoutSkill()
    {
        _settings.Combat.UseSkill = false;
        AddGroundMobs();

        TickFor(2);

        Assert.Contains("apply 125 80000002", _actions.Calls);
    }

    [Fact]
    public void NormalAttackCountsOnlyWhenMobTargetsUs()
    {
        _settings.Combat.AttackSkillId = 0;
        AddGroundMobs();

        TickFor(5);
        Assert.Contains("attack", _actions.Calls);
        Assert.DoesNotContain("select 80000003", _actions.Calls);

        var wolf = _world.Npcs.First(n => n.Wid == 0x80000002);
        _world.Npcs[_world.Npcs.IndexOf(wolf)] = wolf with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking };
        TickFor(1);
        Assert.Contains("select 80000003", _actions.Calls);
    }

    [Fact]
    public void SecondDeathAtDeathPointThenBackToFarm()
    {
        _settings.Target.UseMobList = true;
        _settings.Target.MobNames = ["Волк"];
        _settings.ExpDrain.DeathPoints = [FarmPoint.At("Смерть", new Position(100, 0, 0))];
        _settings.ExpDrain.DeathPoint = "Смерть";
        // В точке для смертей — моб не из списка фарма: там фильтр фарма ни при чём
        _world.Npcs.Add(new NpcInfo(0x80000009, 0x80000009, NpcKind.Mob, NpcState.Standing, 0, new Position(104, 0, 0), "Чужак", 0));
        AddGroundMobs();

        // Первая смерть — на месте фарма, мобы — из списка
        TickFor(3);
        Assert.Contains("select 80000002", _actions.Calls);
        Assert.DoesNotContain("select 80000003", _actions.Calls);
        _world.Hp = 0;
        _world.Exp = 700;

        // Опыта ещё много — летим в точку для смертей
        TickFor(12);
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(100, 0, 0)) <= 5);
        Assert.Contains("select 80000009", _actions.Calls);

        // Слили — на место фарма
        _world.Hp = 0;
        _world.Exp = 50;
        TickFor(12);
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= 5);
        Assert.True(Logged("Слив опыта закончен"));
        Assert.Null(_stop);
    }

    [Fact]
    public void OneDeathEnoughFliesStraightToFarm()
    {
        _settings.ExpDrain.DeathPoints = [FarmPoint.At("Смерть", new Position(100, 0, 0))];
        _settings.ExpDrain.DeathPoint = "Смерть";
        PullTwo();
        _world.Hp = 0;
        _world.Exp = 50;

        TickFor(12);

        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= 5);
        Assert.DoesNotContain(_actions.FlyPoints, p => p.HorizontalDistanceTo(new Position(100, 0, 0)) <= 5);
        Assert.True(Logged("Слив опыта закончен"));
    }

    [Fact]
    public void FlyingMobsOnlyLeaveGroundMobsAlone()
    {
        _settings.ExpDrain.DeathMobs = DrainMobs.Air;
        _world.Flying = true;
        _world.Position = new Position(0, 200, 0);
        _world.Npcs.Add(new NpcInfo(0x80000001, 0x80000001, NpcKind.Mob, NpcState.Standing, 0, new Position(5, 200, 0), "Змея", 0) { Habitat = Habitat.Air });
        var wolf = _world.AddMob(0x80000002, "Волк", 10);
        _world.Replace(wolf, m => m with { Habitat = Habitat.Ground });

        TickFor(3);

        Assert.Contains("select 80000001", _actions.Calls);
        Assert.DoesNotContain("select 80000002", _actions.Calls);
        Assert.DoesNotContain("fly-toggle", _actions.Calls);
        Assert.True(Logged("собрал 1"));
    }

    [Fact]
    public void GroundMobsOnlyLeaveKnownFlyersAlone()
    {
        _settings.ExpDrain.DeathMobs = DrainMobs.Ground;
        var bird = _world.AddMob(0x80000005, "Птица", 5);
        _world.Replace(bird, m => m with { Habitat = Habitat.Air });
        AddGroundMobs();

        TickFor(3);

        Assert.DoesNotContain("select 80000005", _actions.Calls);
        Assert.Contains("select 80000002", _actions.Calls);
    }

    [Fact]
    public void FlyingMobsWantedTakesOffFirst()
    {
        _settings.ExpDrain.DeathMobs = DrainMobs.Air;
        _world.Npcs.Add(new NpcInfo(0x80000001, 0x80000001, NpcKind.Mob, NpcState.Standing, 0, new Position(5, 20, 0), "Змея", 0) { Habitat = Habitat.Air });

        TickFor(3);

        Assert.Equal("fly-toggle", _actions.Calls[0]);
        Assert.Contains("select 80000001", _actions.Calls);
    }
}

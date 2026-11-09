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

/// <summary>Режим «Собирать ресурсы»: обход точек по кругу, копание у точки, только защита.</summary>
public class RouteScenarioTests
{
    private const uint Pickaxe = 3073;
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;
    private int _routeStart;
    private string? _stopped;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public RouteScenarioTests()
    {
        _settings.Mode = BotMode.GatherResources;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _world.Bag.Add(new InventoryItem(5, Pickaxe, 0, 1, null, null));
    }

    private IBotRunner Brain
    {
        get
        {
            if (_brain is not null)
                return _brain;
            _brain = BotModes.Create(BotMode.GatherResources, new ActionRunner(new GameControl(_actions), NullLogger.Instance), Skills, _settings,
                new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), [Pickaxe], _routeStart, new Random(1));
            _brain.StopRequested += reason => _stopped = reason;
            return _brain;
        }
    }

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    private void Route(params float[] xs)
        => _settings.Route.Points = xs.Select((x, i) => RoutePoint.At($"Точка {i + 1}", new Position(x, 0, 0))).ToList();

    private GroundItem AddResource(uint id, float x, string name)
    {
        var item = new GroundItem(0, id, 3079, GroundItemKind.Resource, new Position(x, 0, 0), name);
        _world.Ground.Add(item);
        return item;
    }

    [Fact]
    public void PointsAreVisitedInOrderAndAfterLastComesFirst()
    {
        Route(50, 100);

        Tick();
        _world.Position = new Position(50, 0, 0);
        Tick(); // долетели — ищем ресурсы у точки
        Tick(); // нечего — к следующей
        _world.Position = new Position(100, 0, 0);
        Tick();
        Tick();

        Assert.Equal(
            ["move (50,0; 0,0; h 0,0) умно", "move (100,0; 0,0; h 0,0) умно", "move (50,0; 0,0; h 0,0) умно"],
            _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("На точке 1/2 — ищу: все ресурсы"));
        Assert.Contains(_log, e => e.Message.StartsWith("Точка 2 — здесь всё; дальше 1/2"));
    }

    [Fact]
    public void StartsFromChosenPoint()
    {
        Route(50, 100, 150);
        _routeStart = 2;

        Tick();

        Assert.Equal(["move (150,0; 0,0; h 0,0) умно"], _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("Обход: 3 точек, начинаю с 3-й"));
    }

    [Fact]
    public void StartedInAirFliesAtPointHeightAndTakesOffAgainIfLanded()
    {
        _settings.Route.Points = [RoutePoint.At("Над рудой", new Position(50, 30, 0)), RoutePoint.At("Дальше", new Position(100, 30, 0))];
        _world.Flying = true;
        // Уже на высоте точек — сразу к ней, без подъёма
        _world.Position = new Position(0, 30, 0);

        Tick();
        Assert.Equal(["fly (50,0; 0,0; h 30,0)"], _actions.Calls);

        // Долетели, но оказались на земле — к следующей точке снова в воздух
        _world.Position = new Position(50, 0, 0);
        _world.Flying = false;
        Tick();
        Tick();
        Assert.Equal("fly-toggle", _actions.Calls.Last());
    }

    [Fact]
    public void DigsOnlyListedResourcesNearCurrentPoint()
    {
        Route(0, 300);
        _settings.Route.Points[0].ListMode = LootListMode.OnlyListed;
        _settings.Route.Points[0].Resources = ["Железная руда"];
        AddResource(0xC0000001, 10, "Шалфей");          // не в списке
        AddResource(0xC0000002, 80, "Железная руда");   // дальше радиуса (50 м) от точки
        AddResource(0xC0000003, 30, "Железная руда");

        Tick();
        Tick();

        Assert.Equal(["gather C0000003"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Бегу к Железная руда у точки 1/2, 30,0 м");
    }

    [Fact]
    public void ExceptListedSkipsDangerousResourceAtThisPointOnly()
    {
        // У точки 1 «Шалфей» часто у агро моба — «всё, кроме шалфея»; у точки 2 его копаем
        Route(0, 300);
        _settings.Route.Points[0].ListMode = LootListMode.ExceptListed;
        _settings.Route.Points[0].Resources = ["Шалфей"];
        AddResource(0xC0000001, 10, "Шалфей");
        AddResource(0xC0000002, 30, "Железная руда");

        Tick();
        Tick();

        Assert.Equal(["gather C0000002"], _actions.Calls);
        Assert.True(_settings.Route.Points[1].Wants("Шалфей"));
    }

    private NpcInfo AddAggressive(uint wid, string name, float x, int level, int aggro = 8, float height = 0)
    {
        var mob = new NpcInfo(wid, wid, NpcKind.Mob, NpcState.Standing, 0, new Position(x, height, 0), name, 0)
            { Level = level, Aggressive = true, AggroRadius = aggro };
        _world.Npcs.Add(mob);
        return mob;
    }

    [Fact]
    public void ResourceInDangerousMobZoneIsSkipped()
    {
        // Ближний шалфей у агрессивного моба 30 уровня (агро 8 + запас 1 = 9 м) — копаем дальнюю руду
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        AddResource(0xC0000001, 10, "Шалфей");
        AddResource(0xC0000002, 30, "Железная руда");
        AddAggressive(0x80000001, "Тигр", 18, level: 30);

        Tick();
        Tick();

        Assert.Equal(["gather C0000002"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Не копаю Шалфей: рядом опасный Тигр (ур. 30, агро 8 м) — 8 м от ресурса");
    }

    [Fact]
    public void WeakAggressiveMobAndPassiveBossAreNotDangerous()
    {
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _settings.Route.DangerMobs = ["Король пауков"];
        AddResource(0xC0000001, 10, "Шалфей");
        AddResource(0xC0000002, 30, "Железная руда");
        AddAggressive(0x80000001, "Волк", 12, level: 20);                       // слабее порога
        _world.Npcs.Add(new NpcInfo(0x80000002, 0x80000002, NpcKind.Mob, NpcState.Standing, 0, new Position(28, 0, 0), "Король пауков", 0)
            { Level = 40, Aggressive = false, AggroRadius = 8 });                // из списка, но пассивный

        Tick();
        Tick();
        _world.Ground.RemoveAt(0);
        Tick();

        Assert.Equal(["gather C0000001", "gather C0000002"], _actions.Calls.Where(a => a.StartsWith("gather")));
    }

    [Fact]
    public void ListedBossIsDangerousAtAnyLevelAndOnlyWithinItsZone()
    {
        // Порог по уровню выключен; босс из списка — 10 уровня. Руда в 9,5 м от него — вне зоны (8 + 1), шалфей в 6 м — в зоне
        Route(0, 300);
        _settings.Route.DangerMobs = ["Король пауков"];
        AddResource(0xC0000001, 14, "Шалфей");
        AddResource(0xC0000002, 29.5f, "Железная руда");
        AddAggressive(0x80000001, "Король пауков", 20, level: 10);

        Tick();
        Tick();

        Assert.Equal(["gather C0000002"], _actions.Calls);
    }

    [Fact]
    public void MobHighAboveResourceDoesNotGuardIt()
    {
        // Зона — цилиндр: по высоте агро + запас (9 м) вверх и вниз. Моб на скале в 20 м над ресурсом его не сторожит
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        AddResource(0xC0000001, 10, "Шалфей");
        AddAggressive(0x80000001, "Орёл", 12, level: 30, height: 20);

        Tick();
        Tick();

        Assert.Equal(["gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void DangerousAttackerMakesBotRecallPetTakeOffAndClimbInsteadOfFighting()
    {
        // На земле у точки 1 напал опасный тигр (бьёт пета): отозвать пета, взлететь, подниматься шагами по 10 м — не бить
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = false;
        _world.SetPets(1, new PetInCage(1, 1, 0));
        Tick();
        Tick(); // на точке 1
        var tiger = AddAggressive(0x80000001, "Тигр", 5, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = _world.Pet!.ActiveWid, State = NpcState.Attacking });
        _actions.Calls.Clear();

        Tick();
        Assert.Equal(["recall"], _actions.Calls);
        _world.SetPets(null, new PetInCage(1, 1, 0));
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid });
        Tick();
        Assert.Equal("fly-toggle", _actions.Calls.Last());
        _world.Flying = true;
        Tick();
        Tick();

        Assert.Equal("fly (0,0; 0,0; h 10,0)", _actions.Calls.Last());
        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack") || a.StartsWith("apply"));
        Assert.Contains(_log, e => e.Message == "Напал опасный Тигр (ур. 30) — улетаю вверх");
    }

    [Fact]
    public void AfterDangerousMobGivesUpBotReturnsToLastVisitedPoint()
    {
        // Ушли от тигра вверх у точки 1, он отстал (бросил цель) — снова к точке 1 (на её высоту) и ищем ресурсы там заново
        _settings.Route.Points = [RoutePoint.At("1", new Position(0, 30, 0)), RoutePoint.At("2", new Position(300, 30, 0))];
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        _world.Position = new Position(0, 30, 0);
        Tick();
        Tick(); // на точке 1
        var tiger = AddAggressive(0x80000001, "Тигр", 5, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        _world.Position = new Position(0, 70, 0);
        _world.Replace(tiger, m => m with { TargetWid = 0, State = NpcState.Standing });
        _actions.Calls.Clear();

        Tick();
        Tick();

        Assert.Equal(["fly (0,0; 0,0; h 30,0)"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Тигр отстал (бросил цель) на высоте +40 м — возвращаюсь на точку");
    }

    [Fact]
    public void ReturningDangerousMobEndsEscapeAtOnce()
    {
        // Отагр виден сразу: моб «возвращается», хотя цель ещё показывает перса
        _settings.Route.Points = [RoutePoint.At("1", new Position(0, 30, 0)), RoutePoint.At("2", new Position(300, 30, 0))];
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        _world.Position = new Position(0, 30, 0);
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 5, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        _world.Position = new Position(0, 40, 0);
        _world.Replace(tiger, m => m with { Returning = true, State = NpcState.Moving });
        _actions.Calls.Clear();

        Tick();
        Tick();

        Assert.Equal(["fly (0,0; 0,0; h 30,0)"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Тигр отстал (возвращается) на высоте +10 м — возвращаюсь на точку");
    }

    [Fact]
    public void ClimbsOnlyWhileHitThenHovers()
    {
        // Поднялись на шаг, моб больше не бьёт (стоит внизу, цель — мы) — выше не лезем, висим
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        _world.Position = new Position(0, 10, 0);
        _world.Replace(tiger, m => m with { State = NpcState.Standing });
        Tick(4);
        _actions.Calls.Clear();

        Tick();
        Tick(5);

        Assert.Empty(_actions.Calls);
        Assert.StartsWith("вишу на +10 м", Brain.Part<EscapeBehavior>()!.Status);
    }

    [Fact]
    public void FarMobStuckInAttackStateDoesNotMakeBotClimbHigher()
    {
        // Поднялись на 35 м, моб внизу всё «бьёт» (состояние залипло, цель — мы), но ударов нет — выше не лезем, висим
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        _world.Position = new Position(0, 35, 0);
        Tick(4);
        _actions.Calls.Clear();

        Tick();
        Tick(5);

        Assert.Empty(_actions.Calls);
        Assert.StartsWith("вишу на +35 м", Brain.Part<EscapeBehavior>()!.Status);
    }

    [Fact]
    public void RealHitFromFarMobMakesBotClimbHigher()
    {
        // Моб далеко внизу, но по нам пришёл удар с уроном (дальний каст) — поднимаемся ещё
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        _world.Position = new Position(0, 35, 0);
        Tick(4);
        Tick();
        Tick(5); // висим
        _actions.Calls.Clear();
        _world.Attacks.Add(new AttackEvent(1, 0, tiger.Wid, 0, [new AttackHit(FakeWorld.HostWid, 200, 0)]));

        Tick();

        Assert.Equal(["fly (0,0; 0,0; h 45,0)"], _actions.Calls);
    }

    [Fact]
    public void StuckTargetWithoutHitsCountsAsShakenOff()
    {
        // Ударил, мы ушли вверх; цель застряла на нас (клиент не узнал об отагре), моб 30 с не бьёт — отстал
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        _world.Replace(tiger, m => m with { State = NpcState.Standing });
        _world.Position = new Position(0, 10, 0);
        for (var i = 0; i < 8; i++)
            Tick(5);

        Assert.Contains(_log, e => e.Message.StartsWith("Тигр отстал (не бьёт 30 с)"));
    }

    [Fact]
    public void AtPointWithoutFlyerBotLeavesDangerousAttackerForNextPoint()
    {
        // Стоим на точке 1, напал опасный, полётника нет — не дерёмся, бросаем точку и идём к следующей
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.HasFlyer = false;
        Tick(); // на точке 1
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        _actions.Calls.Clear();

        Tick();
        Tick();

        Assert.Equal(["move (300,0; 0,0; h 0,0) умно"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Точка 1 — напал Тигр; дальше 2/2");
    }

    [Fact]
    public void ShakenDangerousMobWithStuckTargetIsNotFought()
    {
        // Отстал (30 с не бьёт), а цель у него всё ещё на нас: бой не принимает его за напавшего — не берём в цель и не бьём
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Tick();
        _world.Replace(tiger, m => m with { State = NpcState.Standing });
        _world.Position = new Position(0, 10, 0);
        for (var i = 0; i < 8; i++)
            Tick(5);
        for (var i = 0; i < 20; i++)
            Tick();

        Assert.Contains(_log, e => e.Message.StartsWith("Тигр отстал (не бьёт 30 с)"));
        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack") || a.StartsWith("skill"));
    }

    [Fact]
    public void DangerousMobWithStuckTargetDoesNotScareBot()
    {
        // Опасный с застрявшей после старого отагра целью: NpcTracker её снимает (цель 0) — не напал, уходить незачем, копаем
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = true;
        AddResource(0xC0000001, 30, "Железная руда");
        AddAggressive(0x80000001, "Тигр", 60, level: 30);

        Tick();
        Tick();

        Assert.Equal(["gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void WeakAttackerIsFoughtAsBefore()
    {
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        Tick();
        Tick();
        var wolf = AddAggressive(0x80000001, "Волк", 3, level: 20);
        _world.Replace(wolf, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        _actions.Calls.Clear();

        Tick();

        Assert.Equal(["select 80000001"], _actions.Calls);
    }

    [Fact]
    public void AvoidFightsMakesBotFlyFromWeakPassiveAttacker()
    {
        // «Не драться»: напал пассивный слабый волк — не бьём, а улетаем вверх, как от опасного
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _settings.Route.AvoidFights = true;
        _world.Flying = true;
        Tick();
        Tick();
        _world.AddMob(0x80000001, "Волк", 3, targetWid: FakeWorld.HostWid);
        _actions.Calls.Clear();

        Tick();
        Tick();

        Assert.Equal(["fly (0,0; 0,0; h 10,0)"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Напал Волк (ур. 0), «Не драться» — улетаю вверх");
    }

    [Fact]
    public void AvoidFightsIgnoresAttackerAlreadyInTarget()
    {
        // «Не драться»: напавший уже у нас в цели — всё равно не бьём
        Route(0, 300);
        _settings.Route.AvoidFights = true;
        _world.Flying = true;
        Tick();
        Tick();
        var wolf = _world.AddMob(0x80000001, "Волк", 3, targetWid: FakeWorld.HostWid);
        _world.TargetWid = wolf.Wid;
        _actions.Calls.Clear();

        for (var i = 0; i < 8; i++)
            Tick();

        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack") || a.StartsWith("skill"));
        Assert.Contains(_log, e => e.Message.StartsWith("Напал Волк"));
    }

    [Fact]
    public void AvoidFightsTurnedOnMidFightDropsTheFight()
    {
        // Дрались со слабым волком, включили «Не драться» — бой бросаем, от волка уходим
        Route(0, 300);
        _world.Flying = true;
        Tick();
        Tick();
        var wolf = _world.AddMob(0x80000001, "Волк", 3, targetWid: FakeWorld.HostWid);
        Tick();
        _world.TargetWid = wolf.Wid;
        Tick();
        Assert.Contains(_log, e => e.Message.StartsWith("Бой: Волк"));
        _settings.Route.AvoidFights = true;
        Brain.UpdateSettings(_settings);
        _actions.Calls.Clear();

        Tick();
        Tick();

        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack") || a.StartsWith("skill"));
        Assert.Contains("fly (0,0; 0,0; h 10,0)", _actions.Calls);
    }

    [Fact]
    public void WithoutFlightBotLeavesDangerousAttackerForNextPoint()
    {
        // Полёта нет (поле не найдено для сервера) — уйти вверх нельзя; с опасным всё равно не дерёмся — к следующей точке
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.Flying = null;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        _actions.Calls.Clear();

        Tick();

        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack"));
        Assert.StartsWith("иду к точке 2/2", Brain.Part<RouteBehavior>()!.Status);
        Assert.Contains(_log, e => e.Message.StartsWith("Не ушёл вверх от Тигр") && e.Message.EndsWith("не дерусь, ухожу к следующей точке"));
    }

    [Fact]
    public void WithoutFlightMountBotLeavesWhenTakeoffNeverHappens()
    {
        // Полётника нет: клиент на «Полёт» молча ничего не делает — взлёт не подтвердился за 5 с, значит, уходим к следующей
        // точке, а не жмём «Полёт» снова минуту, пока моб бьёт; не дерёмся
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        _actions.Calls.Clear();

        for (var i = 0; i < 24; i++) // 6 с: взлёт ждёт подтверждения 5 с
            Tick();

        Assert.Equal("fly-toggle", _actions.Calls.First());
        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack"));
        Assert.Contains(_log, e => e.Message.StartsWith("Не ушёл вверх от Тигр"));
    }

    [Fact]
    public void KnownNoFlyerBotLeavesWithoutPressingFly()
    {
        // Знаем, что полётника нет: «Полёт» не жмём совсем — сразу к следующей точке, не ждём 5 с под ударами
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _world.HasFlyer = false;
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        _actions.Calls.Clear();

        Tick();
        Tick();

        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack"));
        Assert.StartsWith("иду к точке 2/2", Brain.Part<RouteBehavior>()!.Status);
        Assert.Contains(_log, e => e.Message.Contains("полётника нет"));
    }

    [Fact]
    public void InAirRouteWithoutFlyerGoesOnFoot()
    {
        _settings.Route.Points = [RoutePoint.At("Над рудой", new Position(50, 30, 0)), RoutePoint.At("Дальше", new Position(100, 30, 0))];
        _world.Flying = true;
        Tick();

        // Оказались на земле, а полётника больше нет — по земле, «Полёт» не жмём
        _world.Position = new Position(50, 0, 0);
        _world.Flying = false;
        _world.HasFlyer = false;
        Tick();
        Tick();

        Assert.DoesNotContain("fly-toggle", _actions.Calls);
        Assert.EndsWith("умно", _actions.Calls.Last());
    }

    [Fact]
    public void TakeoffNotSentBotLeavesAtOnce()
    {
        // Взлёт не отправился вовсе (клиент отказал) — сразу к следующей точке, не ждём
        Route(0, 300);
        _settings.Route.DangerLevel = 25;
        _actions.Refuse.Add("fly-toggle");
        Tick();
        Tick();
        var tiger = AddAggressive(0x80000001, "Тигр", 3, level: 30);
        _world.Replace(tiger, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        _actions.Calls.Clear();

        Tick();
        Tick();

        Assert.Equal("fly-toggle", _actions.Calls.First());
        Assert.DoesNotContain(_actions.Calls, a => a.StartsWith("select") || a.StartsWith("attack"));
        Assert.Contains(_log, e => e.Message.StartsWith("Не ушёл вверх от Тигр"));
    }

    [Fact]
    public void AfterDiggingBotReturnsToPointAndFliesToNextFromIt()
    {
        // Точки в воздухе на 30 м; выкопали руду у земли в 30 м от точки — к следующей не от руды, а сначала назад на точку
        _settings.Route.Points = [RoutePoint.At("1", new Position(0, 30, 0)), RoutePoint.At("2", new Position(300, 30, 0))];
        _world.Flying = true;
        _world.Position = new Position(0, 30, 0);
        var ore = AddResource(0xC0000001, 30, "Железная руда");
        Tick();
        Tick();
        // Сначала над рудой на своей высоте, потом копать
        Assert.Equal(["fly (30,0; 0,0; h 30,0)"], _actions.Calls);
        _world.Position = new Position(30, 30, 0);
        Tick();
        Tick();
        Assert.Equal(["fly (30,0; 0,0; h 30,0)", "gather C0000001"], _actions.Calls);

        _world.Position = new Position(30, 0, 0);
        _world.Gather = new GatherProgress(true, 1000, 5000);
        Tick();
        _world.Gather = new GatherProgress(false, 5000, 5000);
        _world.Ground.Remove(ore);
        Tick();
        Tick();
        // Ниже точки на 30 м — сначала вертикально вверх на месте, потом на её высоте к ней (не наискосок)
        Assert.Equal("fly (30,0; 0,0; h 30,0)", _actions.Calls.Last());
        Assert.Contains(_log, e => e.Message == "Точка 1 — здесь всё; возвращаюсь на неё, от неё — к следующей");
        Assert.Contains(_log, e => e.Message == "Поднимаюсь на высоту точки 1 (+30 м), потом к ней");

        _world.Position = new Position(30, 30, 0);
        Tick();
        Tick();
        Assert.Equal("fly (0,0; 0,0; h 30,0)", _actions.Calls.Last());

        _world.Position = new Position(0, 30, 0);
        Tick();
        Tick();

        Assert.Equal("fly (300,0; 0,0; h 30,0)", _actions.Calls.Last());
        Assert.Equal(1, _actions.Calls.Count(c => c.StartsWith("gather")));
    }

    // Над рудой (в 30 м от точки) на высоте точки, вниз, выкопали — руда пропала; перс у земли
    private void DigOreFromAbove()
    {
        var ore = AddResource(0xC0000001, 30, "Железная руда");
        Tick();
        Tick();
        _world.Position = new Position(30, 30, 0);
        Tick();
        Tick();
        Assert.Equal(["fly (30,0; 0,0; h 30,0)", "gather C0000001"], _actions.Calls);
        _world.Position = new Position(30, 0, 0);
        _world.Gather = new GatherProgress(true, 1000, 5000);
        Tick();
        _world.Gather = new GatherProgress(false, 5000, 5000);
        _world.Ground.Remove(ore);
        _actions.Calls.Clear();
    }

    [Fact]
    public void AfterDiggingNextResourceAtPointIsReachedFromPointHeight()
    {
        // Выкопали руду у земли, у точки есть ещё камень: сначала вертикально вверх на высоту точки, оттуда над камнем, потом копать
        HoverAtPoint();
        AddResource(0xC0000002, -40, "Залежи камня");
        DigOreFromAbove();

        Tick();
        Assert.Equal(["fly (30,0; 0,0; h 30,0)"], _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("Поднимаюсь на высоту точки (+30 м), потом над Залежи камня"));

        _world.Position = new Position(30, 30, 0);
        Tick();
        Tick();
        Assert.Equal(["fly (30,0; 0,0; h 30,0)", "fly (-40,0; 0,0; h 30,0)"], _actions.Calls);

        _world.Position = new Position(-40, 30, 0);
        Tick();
        Tick();
        Assert.Equal("gather C0000002", _actions.Calls.Last());
    }

    [Fact]
    public void NextResourceRightBesideIsDugWithoutClimb()
    {
        // Следующий ресурс в 4 м от выкопанного — вверх незачем, копаем сразу
        HoverAtPoint();
        AddResource(0xC0000002, 34, "Залежи камня");
        DigOreFromAbove();

        Tick();
        Tick();

        Assert.Equal(["gather C0000002"], _actions.Calls);
    }

    // Обход в полёте: точка на 30 м над землёй, перс на ней
    private void HoverAtPoint()
    {
        _settings.Route.Points = [RoutePoint.At("1", new Position(0, 30, 0)), RoutePoint.At("2", new Position(300, 30, 0))];
        _world.Flying = true;
        _world.Position = new Position(0, 30, 0);
    }

    [Fact]
    public void InAirMobBesideResourceButBelowFlightIsPassedOverFromAbove()
    {
        // Тигр (агро 8 + 1) в 10 м от руды на земле: ни руда, ни путь «над рудой на 30 м и вниз» его зону не задевают — копаем
        HoverAtPoint();
        _settings.Route.DangerLevel = 25;
        AddResource(0xC0000001, 30, "Железная руда");
        AddAggressive(0x80000001, "Тигр", 20, level: 30);

        Tick();
        Tick();

        Assert.Equal(["fly (30,0; 0,0; h 30,0)"], _actions.Calls);
    }

    [Fact]
    public void InAirResourceBehindDangerousMobOnTheWayIsSkipped()
    {
        // Летучий опасный моб на высоте полёта между точкой и рудой — путь над рудой через его зону: руду не трогаем, берём камень
        HoverAtPoint();
        _settings.Route.DangerLevel = 25;
        AddResource(0xC0000001, 30, "Железная руда");
        AddResource(0xC0000002, -30, "Залежи камня");
        AddAggressive(0x80000001, "Гарпия", 15, level: 30, height: 30);

        Tick();
        Tick();

        Assert.Equal(["fly (-30,0; 0,0; h 30,0)"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Не копаю Железная руда: по пути к нему опасный Гарпия (ур. 30, агро 8 м)");
    }

    [Fact]
    public void InAirResourceGoneWhileFlyingOverIsDropped()
    {
        HoverAtPoint();
        var ore = AddResource(0xC0000001, 30, "Железная руда");
        Tick();
        Tick();
        Assert.Equal(["fly (30,0; 0,0; h 30,0)"], _actions.Calls);

        // Выкопал кто-то другой — копать нечего
        _world.Position = new Position(15, 30, 0);
        _world.Ground.Remove(ore);
        Tick();
        Tick();

        Assert.Contains(_log, e => e.Message == "Железная руда пропал, пока летел над ним");
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("gather"));
    }

    [Fact]
    public void InAirNotReachingOverResourceTwiceDigsWithoutHover()
    {
        // Упёрлись (стоим на месте) — второй раз над рудой; снова не вышло — копаем, путь выберет игра
        HoverAtPoint();
        AddResource(0xC0000001, 30, "Железная руда");

        for (var i = 0; i < 60 && !_actions.Calls.Any(c => c.StartsWith("gather")); i++)
            Tick();

        Assert.Equal(["fly (30,0; 0,0; h 30,0)", "fly (30,0; 0,0; h 30,0)", "gather C0000001"], _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("Не долетел над Железная руда 2 раза"));
    }

    [Fact]
    public void InAirResourceAtFlightHeightIsDugDirectly()
    {
        // Ресурс на скале почти на высоте полёта — спускаться не к чему, копаем сразу
        HoverAtPoint();
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 3079, GroundItemKind.Resource, new Position(20, 29, 0), "Железная руда"));

        Tick();
        Tick();

        Assert.Equal(["gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void OtherRouteChosenOnTheGoStartsFromFirstPoint()
    {
        // Шли ко 2-й точке маршрута «a»; в окне выбрали маршрут «b» — к его 1-й точке
        _routeStart = 1;
        Route(0, 100, 200);
        _settings.Route.RouteId = "a";
        Tick();
        Assert.Equal(["move (100,0; 0,0; h 0,0) умно"], _actions.Calls);

        Route(50, 300);
        _settings.Route.RouteId = "b";
        Brain.UpdateSettings(_settings);
        Tick();

        Assert.Equal("move (50,0; 0,0; h 0,0) умно", _actions.Calls.Last());
        Assert.Contains(_log, e => e.Message == "Маршрут сменился: 2 точек, начинаю с 1-й");
    }

    [Fact]
    public void DiggingThatDoesNotStartIsTriedThreeTimesThenSkipped()
    {
        // Дошли и стоим, полоски нет — игра не начала копать: ещё раз, и ещё; на третий — бросаем ресурс на минуту
        Route(0, 300);
        AddResource(0xC0000001, 10, "Черное железо");
        _world.Gather = new GatherProgress(false, 0, 0);

        for (var i = 0; i < 80; i++)
            Tick();

        Assert.Equal(3, _actions.Calls.Count(c => c == "gather C0000001"));
        Assert.Contains(_log, e => e.Message == "Черное железо: стоим 4 с, а копание не началось — пробую ещё раз (2/3)");
        Assert.Contains(_log, e => e.Message == "Черное железо: стоим 4 с, а копание не началось — пробую ещё раз (3/3)");
        Assert.Contains(_log, e => e.Message == "Черное железо: стоим 4 с, а копание не началось (3 раза подряд) — не копаю 1 мин");
    }

    [Fact]
    public void InAirClimbIsSentOnceWhileRising()
    {
        // Подъём идёт — не шлём его заново каждый снимок
        _settings.Route.Points = [RoutePoint.At("1", new Position(100, 40, 0))];
        _world.Flying = true;
        _world.Position = new Position(0, 0, 0);

        Tick();
        _world.Position = new Position(0, 10, 0);
        Tick();
        _world.Position = new Position(0, 20, 0);
        Tick();

        Assert.Equal(["fly (0,0; 0,0; h 40,0)"], _actions.Calls);
    }

    [Fact]
    public void InAirAtPointHeightFliesStraightToPoint()
    {
        // Уже на высоте точки (чуть ниже — меньше 3 м) — сразу к ней
        _settings.Route.Points = [RoutePoint.At("1", new Position(100, 40, 0))];
        _world.Flying = true;
        _world.Position = new Position(0, 38, 0);

        Tick();

        Assert.Equal(["fly (100,0; 0,0; h 40,0)"], _actions.Calls);
    }

    [Fact]
    public void InAirClimbNotReachedFliesToPointDirectly()
    {
        // Не поднялись (потолок, скала над головой) — стоим 2,5 с: дальше к точке напрямую, точку не пропускаем
        _settings.Route.Points = [RoutePoint.At("1", new Position(100, 40, 0)), RoutePoint.At("2", new Position(300, 40, 0))];
        _world.Flying = true;
        _world.Position = new Position(0, 0, 0);

        for (var i = 0; i < 20; i++)
            Tick();

        Assert.Equal(["fly (0,0; 0,0; h 40,0)", "fly (100,0; 0,0; h 40,0)"], _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("Не поднялся на высоту точки"));
    }

    [Fact]
    public void DangerMarginIsAtLeastOneMetre()
    {
        var settings = new BotSettings();
        settings.Route.DangerMargin = 0;

        settings.Normalize();

        Assert.Equal(1, settings.Route.DangerMargin);
        Assert.Equal(1, new BotSettings().Route.DangerMargin);
    }

    [Fact]
    public void EmptyListDigsAnyRegularResource()
    {
        Route(0, 300);
        AddResource(0xC0000001, 10, "Шалфей");

        Tick();
        Tick();

        Assert.Equal(["gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void NothingIsDugOnTheWayOnlyAtThePoint()
    {
        // Ресурс у точки виден издалека и в радиусе — но сначала долетаем, потом копаем
        Route(100, 300);
        AddResource(0xC0000001, 90, "Шалфей");

        Tick();
        Assert.Equal(["move (100,0; 0,0; h 0,0) умно"], _actions.Calls);

        _world.Position = new Position(100, 0, 0);
        Tick();
        Tick();
        Assert.Equal("gather C0000001", _actions.Calls.Last());
    }

    [Fact]
    public void ArrivedOnlyWhenFlightToPointEndsThenDigs()
    {
        // Над точкой по горизонтали, но ниже на 20 м — полёт ещё идёт: копание получило бы «занято», а это не «копать нечего».
        // Долетели (с высотой) — копаем камень у точки, к следующей не уходим
        _settings.Route.Points = [RoutePoint.At("1", new Position(50, 40, 0)), RoutePoint.At("2", new Position(200, 40, 0))];
        _world.Flying = true;
        _world.Position = new Position(0, 40, 0);
        Tick();
        _world.Position = new Position(50, 20, 0);
        AddResource(0xC0000001, 80, "Залежи камня");
        Tick();
        Tick();
        Assert.Equal(["fly (50,0; 0,0; h 40,0)"], _actions.Calls);

        _world.Position = new Position(50, 40, 0);
        Tick();
        Tick();
        Tick();
        Assert.Equal(["fly (50,0; 0,0; h 40,0)", "fly (80,0; 0,0; h 40,0)"], _actions.Calls);

        // Над камнем — копать
        _world.Position = new Position(80, 40, 0);
        Tick();
        Tick();

        Assert.Equal(["fly (50,0; 0,0; h 40,0)", "fly (80,0; 0,0; h 40,0)", "gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void OnlyDefendsAgainstAttackers()
    {
        Route(0, 300);
        _world.Position = new Position(0, 0, 0);
        _world.AddMob(0x80000001, "Волк", 10);                                     // мирно стоит — не трогаем
        Tick();
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("select"));

        _world.AddMob(0x80000002, "Кабан", 5, targetWid: FakeWorld.HostWid);     // напал
        Tick();
        Assert.Contains("select 80000002", _actions.Calls);
    }

    [Fact]
    public void DefendsEvenWhenFarmSettingSaysNotToPreferAttackers()
    {
        // «Сначала тех, кто бьёт меня» — настройка фарма; обход отбивается всегда
        _settings.Target.PreferAggressive = false;
        Route(0, 300);
        _world.AddMob(0x80000002, "Кабан", 5, targetWid: FakeWorld.HostWid);

        Tick();

        Assert.Contains("select 80000002", _actions.Calls);
    }

    [Fact]
    public void UnreachablePointIsSkipped()
    {
        Route(50, 100);

        // Перс стоит на месте: бег «упирается» дважды — идём к следующей точке
        for (var i = 0; i < 40; i++)
            Tick();

        Assert.Contains("move (100,0; 0,0; h 0,0) умно", _actions.Calls);
        Assert.Contains(_log, e => e.Message.Contains("не дошли 2 раза подряд, пропускаю"));
    }

    [Fact]
    public void GatheringSummonsPetForWhereHostIs()
    {
        // Пет общий с фармом мобов; стартовали в воздухе — зовём летающего
        _settings.Pet.Enabled = true;
        Route(0, 300);
        _world.Flying = true;
        _world.SetPets(null, new PetInCage(1, 1, 0) { Name = "Скорпион", Habitat = Habitat.Ground },
            new PetInCage(2, 1, 0) { Name = "Пчела", Habitat = Habitat.Air });

        Tick();

        Assert.Equal(["summon 2"], _actions.Calls);
    }

    private static PetInCage Bee() => new(2, 1, 0) { Name = "Пчела", Habitat = Habitat.Ground | Habitat.Air };

    private void PetOnlyForFight()
    {
        _settings.Pet.Enabled = true;
        _settings.Pet.OnlyForFight = true;
        Route(0, 300);
    }

    [Fact]
    public void PetOnlyForFightIsNotSummonedWithoutFight()
    {
        PetOnlyForFight();
        _world.SetPets(null, Bee());

        for (var i = 0; i < 8; i++)
            Tick();

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("summon"));
    }

    [Fact]
    public void AttackedSummonsPetThenRecallsItWhenCalmEvenInFlight()
    {
        PetOnlyForFight();
        _world.SetPets(null, Bee());
        var boar = _world.AddMob(0x80000002, "Кабан", 5, targetWid: FakeWorld.HostWid);

        Tick();
        Assert.Equal(["summon 2"], _actions.Calls);

        // Пет пришёл, кабан убит и исчез; обход полетел дальше — через 3 с без боя пет уходит (перелёт не помеха)
        _world.SetPets(2, Bee());
        _world.Npcs.Remove(boar);
        for (var i = 0; i < 80 && !_actions.Calls.Contains("recall"); i++)
            Tick();

        Assert.Contains("recall", _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Боя нет 3 с — отзываю пета");
    }

    [Fact]
    public void PetIsNotRecalledWhileDigging()
    {
        PetOnlyForFight();
        _world.SetPets(2, Bee());
        _world.Gather = new GatherProgress(true, 1000, 8000);

        for (var i = 0; i < 20; i++)
            Tick();

        Assert.DoesNotContain("recall", _actions.Calls);
    }

    [Theory]
    [InlineData(false, "маршрут пуст")]
    [InlineData(true, "нет кирки в сумке")]
    public void StopsWithReason(bool withPoints, string reason)
    {
        if (withPoints)
        {
            Route(50);
            _world.Bag.Clear();
        }

        Tick();

        Assert.StartsWith(reason, _stopped);
        Assert.Empty(_actions.Calls);
    }
}

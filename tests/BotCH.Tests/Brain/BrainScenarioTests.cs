using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Brain;

/// <summary>Сценарии части 5: последовательность подставных снимков → какие действия отправил мозг.</summary>
public class BrainScenarioTests
{
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private BotBrain? _brain;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    private BotBrain Brain => _brain ??= new BotBrain(
        new ActionRunner(_actions, NullLogger.Instance), Skills, _settings,
        new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), new Random(1), [Pickaxe]);

    private const uint Pickaxe = 3073;

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    private string LastCall => _actions.Calls.Last();

    public BrainScenarioTests()
    {
        // По умолчанию — только то, что проверяет сценарий
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
    }

    // ── Банки ───────────────────────────────────────────────────────────────

    [Fact]
    public void LowHpDrinksWeakestSuitablePotion()
    {
        _settings.Potions.HpPercent = 50;
        _world.Hp = 150; // 30 %
        _world.Level = 4;
        _world.AddPotion(1, 8618, 10, hp: 80, level: 5); // не по уровню
        _world.AddPotion(2, 8616, 10, hp: 50);
        _world.AddPotion(3, 8617, 10, hp: 30);

        Tick();

        Assert.Equal(["use 3"], _actions.Calls);
    }

    [Fact]
    public void PotionNotRepeatedWhileItWorks()
    {
        _settings.Potions.HpPercent = 50;
        _world.Hp = 150;
        _world.AddPotion(3, 8617, 10, hp: 30, seconds: 10);

        Tick();
        _world.Consume(3);
        for (var i = 0; i < 20; i++)
            Tick(0.25); // 5 с — банка ещё действует

        Assert.Single(_actions.Calls);
        Tick(6);
        Assert.Equal(2, _actions.Calls.Count);
    }

    [Fact]
    public void NoPotionsIsLoggedOnce()
    {
        _settings.Potions.HpPercent = 50;
        _world.Hp = 100;

        for (var i = 0; i < 20; i++)
            Tick();

        Assert.Single(_log, e => e.Message.Contains("нет банок"));
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void HostDeadStopsBot()
    {
        string? stop = null;
        Brain.StopRequested += reason => stop = reason;
        _world.Hp = 0;

        Tick();

        Assert.Equal("персонаж погиб", stop);
        Assert.Empty(_actions.Calls);
    }

    // ── Пет ─────────────────────────────────────────────────────────────────

    [Fact]
    public void DeadPetIsRevivedThenSummoned()
    {
        _settings.Pet.Enabled = true;
        _world.SetPet(1, hpRatio: 0, summoned: false);
        _world.AddSkill(329);

        Tick();
        Assert.Equal("cast 329 0", LastCall);

        for (var i = 0; i < 20; i++)
            Tick(0.5); // каст ~12 с — ничего не повторяем
        Assert.Single(_actions.Calls);

        _world.SetPet(1, hpRatio: 0.1f, summoned: false);
        Tick();
        Tick();

        Assert.Equal(["cast 329 0", "summon 1"], _actions.Calls);
    }

    [Fact]
    public void HungryPetFedThenPauseWhileStillHungry()
    {
        _settings.Pet.Enabled = true;
        _world.SetPet(1, hunger: 1);
        _world.AddFood(0, 12842, 150, loyalty: 10);

        Tick();
        Assert.Equal(["use 0"], _actions.Calls);
        _world.Consume(0);

        // Сытость ещё не обновилась — 25 с снимков «голоден», корм не отправляется
        for (var i = 0; i < 100; i++)
            Tick(0.25);
        Assert.Single(_actions.Calls);

        Tick(6);
        Assert.Equal(2, _actions.Calls.Count);
    }

    [Fact]
    public void RefusedFoodIsNotNoFood()
    {
        _settings.Pet.Enabled = true;
        _world.SetPet(1, hunger: 1);
        _world.AddFood(0, 12842, 150, loyalty: 10);

        Tick();
        for (var i = 0; i < 20; i++)
            Tick(0.25); // стопка не уменьшилась — отказ

        Assert.DoesNotContain(_log, e => e.Message.Contains("нет корма"));
        Tick(10);
        Assert.Equal(2, _actions.Calls.Count); // через 10 с — ещё попытка тем же кормом
    }

    [Fact]
    public void LowPetHpIsHealed()
    {
        _settings.Pet.Enabled = true;
        _settings.Pet.HealPercent = 70;
        _world.SetPet(1, hpRatio: 0.5f);
        _world.AddSkill(330);

        Tick();

        Assert.Equal($"apply 330 {FakeWorld.PetWid:X}", LastCall);
    }

    [Fact]
    public void NoPetAtAllMeansNoPetActionsAndNoPetLog()
    {
        // Не друид: петов нет, а галочка «пет» включена по умолчанию
        _settings.Pet.Enabled = true;
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSkill = true;
        _settings.Potions.HpPercent = 90;
        _world.Hp = 300;
        _world.AddPotion(1, 8617, 5, hp: 30);
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 5).Wid;

        for (var i = 0; i < 10; i++)
            Tick();

        Assert.Contains("use 1", _actions.Calls);
        Assert.Contains("apply 299 0", _actions.Calls);
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("summon") || c.StartsWith("pet") || c == "cast 329 0");
        Assert.DoesNotContain(_log, e => e.Message.ToLowerInvariant().Contains("пет"));
    }

    [Fact]
    public void PetDisabledIsIgnoredEvenIfPresent()
    {
        _world.SetPet(1, hpRatio: 0, summoned: false);
        _world.AddSkill(329);

        for (var i = 0; i < 5; i++)
            Tick();

        Assert.Empty(_actions.Calls);
    }

    // ── Цель и бой ──────────────────────────────────────────────────────────

    [Fact]
    public void MobAttackingPetIsTargetedEvenIfNotInList()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.UseMobList = true;
        _settings.Target.MobNames = ["Волк"];
        _world.SetPet(1);
        _world.AddMob(0x80000001, "Волк", 5);
        var bear = _world.AddMob(0x80000002, "Медведь", 12, targetWid: FakeWorld.PetWid);

        Tick();

        Assert.Equal($"select {bear.Wid:X}", LastCall);
    }

    [Fact]
    public void MobListByNameNearestOfThatName()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.UseMobList = true;
        _settings.Target.MobNames = ["волк"];
        _world.AddMob(0x80000001, "Медведь", 5);
        _world.AddMob(0x80000002, "Волк", 25);
        var nearWolf = _world.AddMob(0x80000003, "Волк", 15);
        _world.AddMob(0x80000004, "Волк", 30);

        Tick();

        Assert.Equal($"select {nearWolf.Wid:X}", LastCall);
    }

    [Fact]
    public void MobsOutsideFarmRadiusAreIgnored()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.FarmRadius = 30;
        _world.AddMob(0x80000001, "Волк", 40);
        var inside = _world.AddMob(0x80000002, "Волк", 25);

        Tick();

        Assert.Equal($"select {inside.Wid:X}", LastCall);
    }

    [Fact]
    public void RadiusCountsFromStartNotFromWhereHostWalked()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.FarmRadius = 30;
        Tick(); // точка старта — (0, 0)

        // Перс убежал на 50 м: моб рядом с ним (55 м от старта) — нельзя, моб в 20 м от старта — можно
        _world.Position = new Position(50, 0, 0);
        _world.Npcs.Add(new NpcInfo(0, 0x80000001, NpcKind.Mob, 1, 0, new Position(55, 0, 0), 5, "Волк", 0));
        _world.Npcs.Add(new NpcInfo(0, 0x80000002, NpcKind.Mob, 1, 0, new Position(20, 0, 0), 30, "Волк", 0));
        Tick();

        Assert.Equal("select 80000002", LastCall);
    }

    [Fact]
    public void NothingInRadiusMeansWait()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.FarmRadius = 30;
        _world.AddMob(0x80000001, "Волк", 45);

        Tick();

        Assert.Empty(_actions.Calls);
        Assert.Contains("в радиусе 30 м", Brain.Status);
    }

    [Fact]
    public void AggressorOutsideRadiusIsStillFought()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.FarmRadius = 30;
        var bear = _world.AddMob(0x80000001, "Медведь", 45, targetWid: FakeWorld.HostWid);

        Tick();

        Assert.Equal($"select {bear.Wid:X}", LastCall);
    }

    [Fact]
    public void ZeroRadiusIsUnlimited()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.FarmRadius = 0;
        var far = _world.AddMob(0x80000001, "Волк", 300);

        Tick();

        Assert.Equal($"select {far.Wid:X}", LastCall);
    }

    [Fact]
    public void RestartSetsNewStartPoint()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.FarmRadius = 30;
        _settings.Target.ReturnToCenter = false;
        Tick(); // старт в (0, 0)
        _world.Position = new Position(100, 0, 0);
        _world.Npcs.Add(new NpcInfo(0, 0x80000001, NpcKind.Mob, 1, 0, new Position(110, 0, 0), 10, "Волк", 0));
        Tick();
        Assert.Empty(_actions.Calls);

        Brain.Reset(); // Стоп → Старт уже здесь
        Tick();

        Assert.Equal("select 80000001", LastCall);
    }

    [Fact]
    public void DeadMobsAndNpcsAreNotTargets()
    {
        _settings.Target.KillMobs = true;
        _world.AddMob(0x80000001, "Волк", 3, state: NpcInfo.StateDead);
        _world.Npcs.Add(new NpcInfo(0, 0x80000002, NpcKind.Npc, 1, 0, default, 2, "Отшельник", 0));
        var alive = _world.AddMob(0x80000003, "Волк", 20);

        Tick();

        Assert.Equal($"select {alive.Wid:X}", LastCall);
    }

    [Fact]
    public void SkillNotResentWhileApproaching()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSkill = true;
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 30).Wid;

        for (var i = 0; i < 4; i++)
            Tick(0.3); // перс идёт к цели

        Assert.Equal(["apply 299 0"], _actions.Calls);
    }

    [Fact]
    public void NothingSentWhileCasting()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 3).Wid;
        _world.Casting = true;

        Tick();

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void SwordEveryFiveSeconds()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 3, hp: 500).Wid;

        for (var i = 0; i < 40; i++)
            Tick(0.25); // 10 с

        Assert.Equal(2, _actions.Calls.Count(c => c == "attack"));
    }

    [Fact]
    public void ComeCloserWhenSwordOff()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 8;
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 20).Wid;

        Tick();

        Assert.StartsWith("move", LastCall);
        Assert.Contains("(14,0;", LastCall); // 20 м до моба → точка в 6 м от него (8 − запас 2)
    }

    [Theory]
    [InlineData(ApproachPath.Smart, true)]
    [InlineData(ApproachPath.Direct, false)]
    public void ComeCloserBySelectedPath(ApproachPath path, bool smart)
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ApproachPath = path;
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 20).Wid;

        Tick();

        Assert.StartsWith("move", LastCall);
        Assert.Equal(smart, LastCall.EndsWith(" умно"));
    }

    [Fact]
    public void ComeCloserFirstThenSkill()
    {
        // Строго по очереди: бег и скилл «как кнопкой» разом перебивают работы клиента (падение 1.4.6)
        _settings.Target.KillMobs = true;
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 8;
        _settings.Combat.UseSkill = true;
        _settings.Combat.AttackSkillId = 299;
        _world.AddSkill(299);
        var mob = _world.AddMob(0x80000001, "Волк", 20);
        _world.TargetWid = mob.Wid;

        Tick();
        Tick(1);
        Assert.Single(_actions.Calls, c => c.StartsWith("move"));
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("apply"));

        // Добежали: моб в 7 м — теперь скилл
        _world.Position = new Position(13, 0, 0);
        _world.Replace(mob, m => m with { Distance = 7 });
        Tick();
        Tick();
        Assert.Equal("apply 299 0", LastCall);
    }

    [Fact]
    public void NoRunningWhileSkillPending()
    {
        // Скилл ушёл, моб отошёл дальше — пока скилл ждёт подтверждения, не бежим поверх него
        _settings.Target.KillMobs = true;
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 8;
        _settings.Combat.UseSkill = true;
        _settings.Combat.AttackSkillId = 299;
        _world.AddSkill(299);
        var mob = _world.AddMob(0x80000001, "Волк", 5);
        _world.TargetWid = mob.Wid;
        Tick();
        Tick();
        Assert.Equal("apply 299 0", LastCall);

        _world.Replace(mob, m => m with { Position = new Position(20, 0, 0), Distance = 20 });
        Tick(1);

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("move"));
    }

    [Fact]
    public void ComeCloserRetargetsWhenMobRunsAway()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 8;
        var mob = _world.AddMob(0x80000001, "Волк", 20);
        _world.TargetWid = mob.Wid;
        Tick();
        Assert.Contains("(14,0;", LastCall);

        // Моб отбежал на 2 м — к старой точке бежим дальше; ещё на 5 м — сразу к новому месту
        _world.Replace(mob, m => m with { Position = new Position(22, 0, 0), Distance = 22 });
        Tick(1.5);
        Assert.Single(_actions.Calls, c => c.StartsWith("move"));

        _world.Replace(mob, m => m with { Position = new Position(27, 0, 0), Distance = 27 });
        Tick(1.5);
        Assert.Contains("(21,0;", LastCall);
        Assert.Equal(2, _actions.Calls.Count(c => c.StartsWith("move")));
    }

    [Fact]
    public void PetOrderedToAttackTarget()
    {
        _settings.Target.KillMobs = true;
        _settings.Pet.Enabled = true;
        _world.SetPet(1);
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 10).Wid;

        Tick();

        Assert.Equal("pet-attack 80000001", LastCall);
    }

    [Fact]
    public void MobGivenUpAfterTimeout()
    {
        _settings.Target.KillMobs = true;
        _settings.Target.MobTimeoutSeconds = 120;
        _settings.Combat.UseSword = true;
        var stuck = _world.AddMob(0x80000001, "Волк", 3);
        var other = _world.AddMob(0x80000002, "Волк", 8);
        _world.TargetWid = stuck.Wid;

        Tick();
        Tick(121);

        Assert.Contains("unselect", _actions.Calls);
        _world.TargetWid = 0;
        Tick(2.5);
        Assert.Equal($"select {other.Wid:X}", LastCall);
    }

    // ── Лут ─────────────────────────────────────────────────────────────────

    [Fact]
    public void LootGoesStraightToItemSkippingResources()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Loot.Enabled = true;
        _settings.Loot.Attempts = 4;
        var mob = _world.AddMob(0x80000001, "Волк", 10, hp: 100);
        _world.TargetWid = mob.Wid;
        Tick();

        // Моб умер в 10 м; рядом с ним ресурс и мех
        _world.Replace(mob, m => m with { State = NpcInfo.StateDead });
        _world.TargetWid = 0;
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 3089, GroundItemKind.Resource, new Position(10, 0, 0), 9.5f, "Шахта угля"));
        _world.Ground.Add(new GroundItem(0, 0xC0000002, 8094, GroundItemKind.Item, new Position(11, 0, 0), 11, "Мягкий мех"));
        Tick();

        // К месту смерти не идём — подбор «как мышкой» сам подводит к предмету
        Assert.Equal("pickup-approach C0000002", LastCall);
        Assert.DoesNotContain("pickup-approach C0000001", _actions.Calls);
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("move"));
    }

    [Fact]
    public void LootWaitsWhileCasting()
    {
        // Скилл, заказанный по живому мобу, кастуется уже после его смерти — подбор в это время клиент отбрасывает
        KillMobForLoot();
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 3044, GroundItemKind.Money, new Position(3, 0, 0), 3, "Монета"));
        _world.Casting = true;
        Tick();
        Assert.DoesNotContain("pickup-approach C0000001", _actions.Calls);

        _world.Casting = false;
        Tick();
        Assert.Equal("pickup-approach C0000001", LastCall);
    }

    [Theory]
    [InlineData(5, false)]
    [InlineData(10, true)]
    public void LootRadiusIsCountedFromDeathPlace(int radius, bool picked)
    {
        // Моб умер в (2, 0); предмет в 7 м от места смерти — подбирается только при радиусе больше 7
        _settings.Loot.Radius = radius;
        KillMobForLoot();
        _world.Ground.Add(new GroundItem(0, 0xC0000002, 8094, GroundItemKind.Item, new Position(9, 0, 0), 9, "Мягкий мех"));
        Tick();

        Assert.Equal(picked, _actions.Calls.Contains("pickup-approach C0000002"));
    }

    [Fact]
    public void LootOnlyAroundDeathPlace()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Loot.Enabled = true;
        var mob = _world.AddMob(0x80000001, "Волк", 2, hp: 100);
        _world.TargetWid = mob.Wid;
        Tick();

        // Моб умер в (2, 0); монета с него рядом, старый мех — в 14 м от места смерти (и в 12 м от перса)
        _world.Replace(mob, m => m with { State = NpcInfo.StateDead });
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 3044, GroundItemKind.Money, new Position(3, 0, 0), 3, "Монета"));
        _world.Ground.Add(new GroundItem(0, 0xC0000002, 8094, GroundItemKind.Item, new Position(-12, 0, 0), 12, "Мягкий мех"));
        Tick();
        Assert.Equal("pickup-approach C0000001", LastCall);

        _world.Ground.RemoveAt(0);
        Tick(2);
        Tick();

        Assert.DoesNotContain("pickup-approach C0000002", _actions.Calls);
    }

    private NpcInfo KillMobForLoot()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Loot.Enabled = true;
        var mob = _world.AddMob(0x80000001, "Волк", 2, hp: 100);
        _world.TargetWid = mob.Wid;
        Tick();
        _world.Replace(mob, m => m with { State = NpcInfo.StateDead });
        return mob;
    }

    [Fact]
    public void FullBagPicksOnlyMoneyAndFittingStacks()
    {
        _world.BagSlots = 2;
        _world.Bag.Add(new InventoryItem(0, 8094, 8, 5, null, null) { MaxCount = 99 }); // «Мягкий мех» ×5 из 99
        _world.Bag.Add(new InventoryItem(1, 830, 8, 1, null, null) { MaxCount = 1 });
        KillMobForLoot();
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 8083, GroundItemKind.Item, new Position(2, 0, 0), 1, "Разорванный мех"));
        _world.Ground.Add(new GroundItem(0, 0xC0000002, 8094, GroundItemKind.Item, new Position(3, 0, 0), 2, "Мягкий мех"));
        _world.Ground.Add(new GroundItem(0, 0xC0000003, 3044, GroundItemKind.Money, new Position(4, 0, 0), 3, "Монета"));

        Tick();
        Assert.Equal("pickup-approach C0000002", LastCall); // ляжет в стопку
        _world.Ground.RemoveAt(1);
        Tick(2);
        Tick(1.5); // пауза между подборами 0.7–1.3 с
        Assert.Equal("pickup-approach C0000003", LastCall); // монеты — всегда

        Assert.DoesNotContain("pickup-approach C0000001", _actions.Calls);
        Assert.Contains(_log, e => e.Message.Contains("Сумка полна"));
    }

    [Fact]
    public void TwoFailedItemPickupsMeanOnlyMoneyForAWhile()
    {
        _settings.Loot.Attempts = 10;
        KillMobForLoot();
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 8083, GroundItemKind.Item, new Position(2, 0, 0), 1, "Разорванный мех"));
        _world.Ground.Add(new GroundItem(0, 0xC0000002, 8094, GroundItemKind.Item, new Position(3, 0, 0), 2, "Мягкий мех"));
        _world.Ground.Add(new GroundItem(0, 0xC0000003, 8090, GroundItemKind.Item, new Position(3, 0, 0), 2, "Клык"));
        _world.Ground.Add(new GroundItem(0, 0xC0000004, 3044, GroundItemKind.Money, new Position(4, 0, 0), 3, "Монета"));

        // Два предмета подряд не поднялись (лежат на земле дольше 10 с)
        Tick();
        Tick(10.5);
        Tick(2);
        Tick(10.5);
        Tick(2);

        Assert.Equal("pickup-approach C0000004", LastCall);
        Assert.DoesNotContain("pickup-approach C0000003", _actions.Calls);
        Assert.Contains(_log, e => e.Message.Contains("только монеты"));
    }

    [Fact]
    public void LootFilterBlackList()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Loot.Enabled = true;
        _settings.Loot.ListMode = LootListMode.ExceptListed;
        _settings.Loot.ItemNames = ["Разорванный мех"];
        var mob = _world.AddMob(0x80000001, "Волк", 2, hp: 100);
        _world.TargetWid = mob.Wid;
        Tick();

        _world.Replace(mob, m => m with { State = NpcInfo.StateDead });
        _world.Ground.Add(new GroundItem(0, 0xC0000001, 8083, GroundItemKind.Item, new Position(1, 0, 0), 1, "Разорванный мех"));
        _world.Ground.Add(new GroundItem(0, 0xC0000002, 3044, GroundItemKind.Money, new Position(3, 0, 0), 3, "Монета"));
        Tick();

        Assert.Equal("pickup-approach C0000002", LastCall);
    }

    [Fact]
    public void AggressorInterruptsLoot()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Loot.Enabled = true;
        var mob = _world.AddMob(0x80000001, "Волк", 2, hp: 100);
        _world.TargetWid = mob.Wid;
        Tick();
        _world.Replace(mob, m => m with { State = NpcInfo.StateDead });
        _world.Ground.Add(new GroundItem(0, 0xC0000002, 3044, GroundItemKind.Money, new Position(3, 0, 0), 3, "Монета"));
        Tick();
        Assert.Equal("pickup-approach C0000002", LastCall);

        var attacker = _world.AddMob(0x80000009, "Медведь", 6, targetWid: FakeWorld.HostWid);
        _world.Ground.Clear();
        Tick();
        Tick();

        Assert.Equal($"select {attacker.Wid:X}", LastCall);
    }

    [Fact]
    public void SettingsChangeAppliesOnNextTick()
    {
        _world.AddMob(0x80000001, "Волк", 5);
        Tick();
        Assert.Empty(_actions.Calls);

        _settings.Target.KillMobs = true;
        Brain.UpdateSettings(_settings);
        Tick();

        Assert.Equal("select 80000001", LastCall);
    }

    // ── Копать ресурсы ─────────────────────────────────────────────────────────

    private GroundItem AddOre(uint id, float distance, string name = "Железная руда")
    {
        var ore = new GroundItem(0, id, 3079, GroundItemKind.Resource, new Position(distance, 0, 0), distance, name);
        _world.Ground.Add(ore);
        return ore;
    }

    private void GatherWithPickaxe()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Loot.Enabled = true;
        _settings.Loot.PickResources = true;
        _world.Bag.Add(new InventoryItem(5, Pickaxe, 0, 1, null, null));
    }

    [Fact]
    public void GatherDigsResourcesBeforeMobs()
    {
        GatherWithPickaxe();
        _world.AddMob(0x80000001, "Волк", 8, hp: 100);
        AddOre(0xC0000002, 20);
        AddOre(0xC0000001, 12);

        Tick();

        Assert.Equal(["gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void GatherSkipsResourcesNotInList()
    {
        GatherWithPickaxe();
        _settings.Loot.ListMode = LootListMode.ExceptListed;
        _settings.Loot.ItemNames = ["Железная руда"];
        AddOre(0xC0000001, 5);
        AddOre(0xC0000002, 15, "Залежи камня");

        Tick();

        Assert.Equal(["gather C0000002"], _actions.Calls);
    }

    [Fact]
    public void NoPickaxeNoGatherFightsInstead()
    {
        GatherWithPickaxe();
        _world.Bag.Clear();
        _world.AddMob(0x80000001, "Волк", 8, hp: 100);
        AddOre(0xC0000001, 5);

        Tick();

        Assert.Equal("select 80000001", LastCall);
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("gather"));
        Assert.Contains(_log, e => e.Message.Contains("нет кирки"));
    }

    [Fact]
    public void AttackWhileGatheringDropsItAndFightsThenDigsAgain()
    {
        GatherWithPickaxe();
        var ore = AddOre(0xC0000001, 5);
        Tick();
        Assert.Equal("gather C0000001", LastCall);

        // Моб бьёт перса — копание бросаем, бьём его
        var mob = _world.AddMob(0x80000001, "Волк", 4, targetWid: FakeWorld.HostWid, hp: 100);
        Tick();
        Assert.Equal("select 80000001", LastCall);
        Assert.Contains(_log, e => e.Message.Contains("бросаю копать"));

        // Убили — копаем тот же ресурс снова
        _world.TargetWid = mob.Wid;
        Tick();
        _world.Replace(mob, m => m with { State = NpcInfo.StateDead, TargetWid = 0 });
        _world.TargetWid = 0;
        Tick();
        Tick();

        Assert.Equal($"gather {ore.Id:X}", LastCall);
    }

    [Fact]
    public void GatherKnockedDownThreeTimesSkipsResource()
    {
        GatherWithPickaxe();
        AddOre(0xC0000001, 3);
        AddOre(0xC0000002, 10);
        for (var i = 0; i < 3; i++)
        {
            _world.Gather = new GatherProgress(false, 0, 0);
            Tick();
            Assert.Equal("gather C0000001", LastCall);
            _world.Gather = new GatherProgress(true, 500, 5000);
            Tick();
            _world.Gather = new GatherProgress(false, 1000, 5000);
            Tick();
        }

        Tick();

        Assert.Equal("gather C0000002", LastCall);
        Assert.Contains(_log, e => e.Message.Contains("сбили 3 раза подряд"));
    }

    [Fact]
    public void AfterLootDigsBeforeSelectingNextMob()
    {
        // Лут кончился — следующую цель не выбираем, пока в радиусе есть ресурсы
        GatherWithPickaxe();
        KillMobForLoot();
        _world.Ground.Add(new GroundItem(0, 0xC0000009, 3044, GroundItemKind.Money, new Position(2, 0, 0), 2, "Монета"));
        _world.AddMob(0x80000002, "Волк", 15, hp: 100);
        _world.TargetWid = 0;
        Tick();
        Assert.Equal("pickup-approach C0000009", LastCall);
        _world.Ground.Clear();
        AddOre(0xC0000001, 12);
        Tick();
        Tick(1.5);
        Tick();

        Assert.Equal("gather C0000001", LastCall);
        Assert.DoesNotContain("select 80000002", _actions.Calls);
    }

    // ── Пет важнее всего ──────────────────────────────────────────────────────

    private void PetNeedsHealSoon()
    {
        _settings.Pet.Enabled = true;
        _settings.Pet.HealPercent = 70;
        _world.SetPet(1, hpRatio: 1f);
        _world.AddSkill(330);
    }

    [Fact]
    public void PetHealStopsRunToMob()
    {
        PetNeedsHealSoon();
        _settings.Target.KillMobs = true;
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 8;
        _settings.Combat.UseSword = true;
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 40).Wid;
        Tick();
        Tick();
        Assert.StartsWith("move", LastCall);

        // Бежим, а пету плохо — лечим сразу, не добегая
        _world.SetPet(1, hpRatio: 0.4f);
        Tick();

        Assert.Equal($"apply 330 {FakeWorld.PetWid:X}", LastCall);
    }

    [Fact]
    public void PetHealInterruptsDiggingThenHeals()
    {
        PetNeedsHealSoon();
        GatherWithPickaxe();
        AddOre(0xC0000001, 3);
        Tick();
        Assert.Equal("gather C0000001", LastCall);
        _world.Gather = new GatherProgress(true, 1000, 8000);
        Tick();

        // Копаем, пету плохо — прерываем копание (как Esc), лечить — когда полоска пропала
        _world.SetPet(1, hpRatio: 0.4f);
        Tick();
        Assert.Equal("cancel", LastCall);
        Tick();
        Assert.Equal("cancel", LastCall);

        _world.Gather = new GatherProgress(false, 1500, 8000);
        Tick();
        Assert.Equal($"apply 330 {FakeWorld.PetWid:X}", LastCall);
    }

    [Fact]
    public void PetHealInterruptsCast()
    {
        PetNeedsHealSoon();
        _world.SetPet(1, hpRatio: 0.4f);
        _world.Casting = true;
        _world.CastingSkillId = 299;

        Tick();
        Assert.Equal("cancel", LastCall);

        _world.Casting = false;
        _world.CastingSkillId = 0;
        Tick();
        Assert.Equal($"apply 330 {FakeWorld.PetWid:X}", LastCall);
    }

    [Fact]
    public void WithoutCancelPetWaitsForCastToEnd()
    {
        PetNeedsHealSoon();
        _actions.CanCancel = false;
        _world.SetPet(1, hpRatio: 0.4f);
        _world.Casting = true;
        _world.CastingSkillId = 299;

        Tick();

        Assert.Empty(_actions.Calls);
    }

    // ── Снимать мобов с себя петом ──────────────────────────────────────────────

    [Fact]
    public void MobHittingHostTakesFightFromMobHittingPet()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Pet.Enabled = true;
        _world.SetPet(1);
        var a = _world.AddMob(0x80000001, "Волк", 4, targetWid: FakeWorld.PetWid, hp: 100);
        _world.TargetWid = a.Wid;
        Tick();
        Tick();

        // Пока пет держит А, на перса агрится Б — бой и пет переходят на Б
        _world.AddMob(0x80000002, "Кабан", 6, targetWid: FakeWorld.HostWid, hp: 100);
        Tick();
        Assert.Equal("select 80000002", LastCall);
        Assert.Contains(_log, e => e.Message.Contains("перевожу бой и пета"));

        _world.TargetWid = 0x80000002;
        Tick();
        Assert.Contains("pet-attack 80000002", _actions.Calls);
    }

    [Fact]
    public void WithoutPetMobHittingPetIsNotLeft()
    {
        // Пет не призван — правило не действует: текущий бьёт перса, второй тоже, не мечемся
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        var a = _world.AddMob(0x80000001, "Волк", 4, targetWid: FakeWorld.HostWid, hp: 100);
        _world.TargetWid = a.Wid;
        Tick();
        _world.AddMob(0x80000002, "Кабан", 3, targetWid: FakeWorld.HostWid, hp: 100);
        Tick();
        Tick();

        Assert.DoesNotContain("select 80000002", _actions.Calls);
    }

    [Fact]
    public void PetTakesAggroOffKeepsFightingMobHittingPet()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        _settings.Pet.Enabled = true;
        _settings.Target.PetTakesAggro = false;
        _world.SetPet(1);
        var a = _world.AddMob(0x80000001, "Волк", 4, targetWid: FakeWorld.PetWid, hp: 100);
        _world.TargetWid = a.Wid;
        Tick();
        _world.AddMob(0x80000002, "Кабан", 6, targetWid: FakeWorld.HostWid, hp: 100);
        Tick();
        Tick();

        Assert.DoesNotContain("select 80000002", _actions.Calls);
    }

    [Fact]
    public void PetHealDoesNotInterruptItself()
    {
        // Подтверждение пришло или вышло время, а каст лечения ещё идёт — это тот же скилл, отмену не шлём
        PetNeedsHealSoon();
        _world.SetPet(1, hpRatio: 0.4f);
        Tick();
        Assert.Equal($"apply 330 {FakeWorld.PetWid:X}", LastCall);

        _world.Casting = true;
        _world.CastingSkillId = 330;
        Tick(5.5);
        Tick();
        Tick();

        Assert.DoesNotContain("cancel", _actions.Calls);
    }

    [Fact]
    public void UnknownCastIsNotInterrupted()
    {
        // Сервер не показывает, какой скилл кастуется — вдруг это лечение: не сбиваем, пет ждёт
        PetNeedsHealSoon();
        _world.SetPet(1, hpRatio: 0.4f);
        _world.Casting = true;
        _world.CastingSkillId = null;

        Tick();

        Assert.DoesNotContain("cancel", _actions.Calls);
    }

    // ── Центр фарма ─────────────────────────────────────────────────────────

    private void FarmAt(float x, int radius = 20)
    {
        _settings.Target.KillMobs = true;
        _settings.Target.FarmRadius = radius;
        _settings.Target.FarmPoints = [FarmPoint.At("Поляна", new Position(x, 0, 0))];
        _settings.Target.FarmCenter = "Поляна";
    }

    // Возврат — только когда делать нечего 2 с подряд
    private void IdleUntilReturn()
    {
        Tick();
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("move"));
        for (var i = 0; i < 8; i++)
            Tick();
    }

    [Fact]
    public void NoReturnRightAfterKillWhenNextMobIsPickedNextTick()
    {
        // Бой отдал ход (после лута), шагом позже нашёл моба — бег в центр не начинаем
        FarmAt(50, radius: 48);
        Tick();
        _world.AddMob(0x80000001, "Волк", 40);
        Tick();

        Assert.Equal(["select 80000001"], _actions.Calls);
    }

    [Fact]
    public void RadiusCountsFromSavedPointNotFromStart()
    {
        // Старт в 0, точка фарма в 100: моб рядом с персом — вне радиуса, моб у точки — цель
        FarmAt(100);
        _settings.Target.ReturnToCenter = false;
        _world.AddMob(0x80000001, "Волк", 10);
        _world.AddMob(0x80000002, "Кабан", 95);

        Tick();

        Assert.Equal(["select 80000002"], _actions.Calls);
    }

    [Fact]
    public void NoTargetsFarFromCenterRunsBack()
    {
        FarmAt(50);

        IdleUntilReturn();

        Assert.Equal(["move (50,0; 0,0; h 0,0) умно"], _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("Вне радиуса фарма, целей нет — возвращаюсь в центр"));
    }

    [Fact]
    public void MobInRadiusBeatsReturn()
    {
        FarmAt(50);
        _world.AddMob(0x80000001, "Волк", 40);

        Tick();

        Assert.Equal(["select 80000001"], _actions.Calls);
    }

    [Fact]
    public void InsideRadiusOrReturnOffStaysPut()
    {
        // Центр — точка старта, перс отошёл на 30 м, но радиус 60: внутри — стоим
        _settings.Target.KillMobs = true;
        Tick();
        _world.Position = new Position(30, 0, 0);
        for (var i = 0; i < 12; i++)
            Tick();
        Assert.Empty(_actions.Calls);

        // Радиус 0 — без ограничения, возвращаться некуда
        _settings.Target.FarmRadius = 0;
        _brain = null;
        _world.Position = new Position(0, 0, 0);
        Tick();
        _world.Position = new Position(400, 0, 0);
        for (var i = 0; i < 12; i++)
            Tick();
        Assert.Empty(_actions.Calls);
        _settings.Target.FarmRadius = 60;

        // Ушёл далеко, но возврат выключен
        _settings.Target.ReturnToCenter = false;
        _brain = null;
        _world.Position = new Position(0, 0, 0);
        Tick();
        _world.Position = new Position(100, 0, 0);
        for (var i = 0; i < 12; i++)
            Tick();
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void FightInterruptsReturn()
    {
        // Бежим в центр (фон) — появился моб: удар скиллом важнее бега
        FarmAt(50, radius: 48);
        _settings.Combat.UseSkill = true;
        _world.AddSkill(299);
        IdleUntilReturn();
        Assert.StartsWith("move", LastCall);

        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 5).Wid;
        Tick();
        Tick();

        Assert.Equal("apply 299 0", LastCall);
    }

    [Fact]
    public void ComeCloserReplacesReturn()
    {
        // Бежим в центр — моб далеко: свой подход к нему вместо бега в центр
        FarmAt(50, radius: 48);
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 8;
        _settings.Combat.ApproachPath = ApproachPath.Direct;
        _settings.Target.ReturnPath = ApproachPath.Direct;
        IdleUntilReturn();
        Assert.Equal("move (50,0; 0,0; h 0,0)", LastCall);

        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 20).Wid;
        Tick();
        Tick();

        Assert.Equal("move (14,0; 0,0; h 0,0)", LastCall);
    }

    [Fact]
    public void ComeCloserMobNearDoesNotWaitForReturn()
    {
        // Бежим в центр — моб уже ближе нужного: бьём сразу, не «добегаем» до центра
        FarmAt(50, radius: 48);
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 8;
        _settings.Combat.UseSkill = true;
        _world.AddSkill(299);
        IdleUntilReturn();
        Assert.StartsWith("move", LastCall);

        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 5).Wid;
        Tick();
        Tick();

        Assert.Equal("apply 299 0", LastCall);
    }

    [Fact]
    public void ReturnPathIsSeparateFromApproach()
    {
        // К мобу — умно, в центр — прямо
        FarmAt(50);
        _settings.Combat.ApproachPath = ApproachPath.Smart;
        _settings.Target.ReturnPath = ApproachPath.Direct;

        IdleUntilReturn();

        Assert.Equal(["move (50,0; 0,0; h 0,0)"], _actions.Calls);
    }

    [Fact]
    public void ApproachStopsWithinDistanceNoSecondRun()
    {
        // Перс встал на краю допуска «дошли» (1.5 м до точки, дальше от моба) — всё равно ближе нужного: бьём, не доподходим
        _settings.Target.KillMobs = true;
        _settings.Combat.ComeCloser = true;
        _settings.Combat.ComeCloserDistance = 3;
        _settings.Combat.UseSkill = true;
        _world.AddSkill(299);
        var mob = _world.AddMob(0x80000001, "Скарабей", 20);
        _world.TargetWid = mob.Wid;
        Tick();
        Assert.Equal("move (19,0; 0,0; h 0,0) умно", LastCall); // точка в 1 м от моба (3 − 2)

        _world.Position = new Position(17.5f, 0, 0);
        _world.Replace(mob, m => m with { Distance = 2.5f });
        Tick();
        Tick();

        Assert.Equal("apply 299 0", LastCall);
        Assert.Single(_actions.Calls, c => c.StartsWith("move"));
    }

    [Theory]
    [InlineData(1500, false)] // лечение вот-вот — атаку не начинаем
    [InlineData(10000, true)] // ждать долго — бьём
    public void PetStillLowWaitsForHealInsteadOfAttack(int healCooldownMs, bool attacks)
    {
        PetNeedsHealSoon();
        _world.SetPet(1, hpRatio: 0.4f);
        _world.Skills.RemoveAll(s => s.Id == 330);
        _world.AddSkill(330, healCooldownMs);
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSkill = true;
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(0x80000001, "Волк", 3).Wid;

        Tick();
        Tick();

        Assert.Equal(attacks, _actions.Calls.Contains("apply 299 0"));
    }
}

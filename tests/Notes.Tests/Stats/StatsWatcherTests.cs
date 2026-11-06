using System.Collections.Generic;
using System.Linq;
using Notes.Core.Stats;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Stats;

public class StatsWatcherTests
{
    private readonly FakeWorld _world = new() { BagSlots = 32 };
    private readonly StatsWatcher _watcher = new();
    private readonly List<StatEvent> _events = [];

    private void Tick(double seconds = 0.25, bool draining = false, bool collecting = false)
        => _events.AddRange(_watcher.Observe(_world.Wait(seconds).Snapshot(), draining, collecting));

    private List<StatEvent> Of(StatKind kind) => _events.Where(e => e.Kind == kind).ToList();

    private void AddBag(int slot, uint tid, int count, string name)
        => _world.Bag.Add(new InventoryItem(slot, tid, 0, count, null, null) { Name = name });

    [Fact]
    public void FirstSnapshotStartsSessionAndEndClosesIt()
    {
        Tick();
        var started = _world.Time;
        Tick(10);

        var end = _watcher.End();

        Assert.Equal([StatKind.SessionStart], _events.Select(e => e.Kind));
        Assert.Equal(started, _events[0].Time);
        Assert.Equal(StatKind.SessionEnd, Assert.Single(end).Kind);
        Assert.Equal(_world.Time, end[0].Time);
        Assert.Empty(_watcher.End());
    }

    [Fact]
    public void AliveEveryFewMinutes()
    {
        Tick();
        for (var i = 0; i < 11 * 60; i++)
            Tick(1);

        Assert.Equal(2, Of(StatKind.Alive).Count);
    }

    [Fact]
    public void MobOurTargetThatDiesIsKill()
    {
        var wolf = _world.AddMob(0x80000001, "Волк", 3);
        _world.TargetWid = wolf.Wid;
        Tick();
        Tick();
        _world.TargetWid = 0; // цель снимается чуть раньше флага смерти
        Tick();
        _world.Replace(wolf, m => m with { State = NpcState.Dead });
        Tick();
        Tick();

        Assert.Equal("Волк", Assert.Single(Of(StatKind.Kill)).Name);
    }

    [Fact]
    public void MobThatFightsPetOrPetTargetIsKill()
    {
        _world.SetPet(1);
        var bear = _world.AddMob(0x80000002, "Медведь", 12, targetWid: FakeWorld.PetWid);
        var fox = _world.AddMob(0x80000003, "Лиса", 15);
        _world.Replace(_world.Npcs.Single(n => n.Kind == NpcKind.Pet), p => p with { TargetWid = fox.Wid, State = NpcState.Attacking });
        Tick();
        _world.Replace(bear, m => m with { State = NpcState.Dead });
        _world.Replace(fox, m => m with { State = NpcState.Dead });
        Tick();

        Assert.Equal(["Медведь", "Лиса"], Of(StatKind.Kill).Select(e => e.Name));
    }

    [Fact]
    public void StrangersDeadMobIsNotKill()
    {
        var wolf = _world.AddMob(0x80000001, "Волк", 3);
        Tick();
        _world.Replace(wolf, m => m with { State = NpcState.Dead });
        Tick();

        Assert.Empty(Of(StatKind.Kill));
    }

    [Fact]
    public void LongForgottenTargetIsNotKill()
    {
        var wolf = _world.AddMob(0x80000001, "Волк", 3);
        _world.TargetWid = wolf.Wid;
        Tick();
        _world.TargetWid = 0;
        Tick(30);
        _world.Replace(wolf, m => m with { State = NpcState.Dead });
        Tick();

        Assert.Empty(Of(StatKind.Kill));
    }

    [Fact]
    public void BagGainWhileCollectingIsLoot()
    {
        AddBag(1, 8094, 3, "Мягкий мех");
        Tick();
        Tick(collecting: true);
        _world.Bag[0] = _world.Bag[0] with { Count = 5 };
        AddBag(2, 3089, 1, "Уголь");
        Tick(1); // предмет пришёл после подтверждения подбора — в пределах запаса

        Assert.Equal([("Мягкий мех", 2L), ("Уголь", 1L)], Of(StatKind.Item).Select(e => (e.Name, e.Amount)));
    }

    [Fact]
    public void BagGainWithoutCollectingIsNotLoot()
    {
        Tick();
        Tick(collecting: true);
        Tick(10);
        AddBag(1, 8616, 50, "Банка"); // купили в лавке
        Tick();

        Assert.Empty(Of(StatKind.Item));
    }

    [Fact]
    public void UnreadBagIsNotCompared()
    {
        AddBag(1, 8094, 3, "Мягкий мех");
        Tick();
        _world.BagSlots = 0;
        _world.Bag.Clear();
        Tick(collecting: true);
        _world.BagSlots = 32;
        AddBag(1, 8094, 3, "Мягкий мех");
        Tick(collecting: true);

        Assert.Empty(Of(StatKind.Item));
    }

    [Fact]
    public void MoneyGainWhileCollecting()
    {
        _world.Money = 1000;
        Tick();
        _world.Money = 1022;
        Tick(collecting: true);
        _world.Money = 900; // ремонт
        Tick();
        _world.Money = 950; // продали в лавке
        Tick(10);

        Assert.Equal(22, Assert.Single(Of(StatKind.Money)).Amount);
    }

    [Fact]
    public void DeathCountsOnlyOutsideDrain()
    {
        Tick();
        _world.Hp = 0;
        Tick();
        _world.Hp = 500;
        Tick(draining: true);
        _world.Hp = 0;
        Tick(draining: true);
        _world.Hp = 500;
        Tick(draining: true);
        Tick();

        Assert.Single(Of(StatKind.Death));
        Assert.Single(Of(StatKind.DrainStart));
        Assert.Single(Of(StatKind.DrainEnd));
    }

    [Fact]
    public void StopDuringDrainEndsDrain()
    {
        Tick(draining: true);

        Assert.Equal([StatKind.DrainEnd, StatKind.SessionEnd], _watcher.End().Select(e => e.Kind));
    }

    [Fact]
    public void ExpGainCountsAcrossLevelUp()
    {
        (_world.Level, _world.Exp, _world.ExpToLevel) = (10, 900, 1000);
        Tick();
        _world.Exp = 950;
        Tick();
        (_world.Level, _world.Exp, _world.ExpToLevel) = (11, 30, 1400);
        Tick();
        _world.Exp = -200; // смерть
        Tick();

        Assert.Equal([50L, 80L], Of(StatKind.Exp).Select(e => e.Amount));
        Assert.Equal(11, Assert.Single(Of(StatKind.Level)).Amount);
    }

    [Fact]
    public void ExpResetBeforeLevelChangeIsNotWholeLevel()
    {
        // Как было на 59 → 60: снимок, где опыт уже сброшен, а уровень ещё старый
        (_world.Level, _world.Exp, _world.ExpToLevel) = (59, 439_680, 439_845);
        Tick();
        _world.Exp = 165;
        Tick();
        (_world.Level, _world.ExpToLevel) = (60, 468_000);
        Tick();
        _world.Exp = 531;
        Tick();

        Assert.Equal([165L + 165, 366L], Of(StatKind.Exp).Select(e => e.Amount));
    }

    [Fact]
    public void LevelChangeBeforeExpResetIsNotDoubled()
    {
        (_world.Level, _world.Exp, _world.ExpToLevel) = (59, 439_680, 439_845);
        Tick();
        (_world.Level, _world.Exp, _world.ExpToLevel) = (60, 440_046, 468_000); // уровень новый, опыт ещё старый
        Tick();
        _world.Exp = 201;
        Tick();

        Assert.Equal(165 + 201, Of(StatKind.Exp).Sum(e => e.Amount));
    }

    [Fact]
    public void ExpLostOnDeathIsNotSubtractedAndGainsCountAfter()
    {
        (_world.Level, _world.Exp, _world.ExpToLevel) = (59, 200_000, 439_845);
        Tick();
        _world.Exp = 180_000; // смерть
        Tick();
        Tick(5);
        _world.Exp = 180_366;
        Tick();

        Assert.Equal([366L], Of(StatKind.Exp).Select(e => e.Amount));
    }

    [Fact]
    public void RepairIsDurabilityGrowth()
    {
        _world.Equipment.Add(new InventoryItem(0, 100, 0, 1, null, null) { Durability = 2000, MaxDurability = 6600 });
        _world.Equipment.Add(new InventoryItem(1, 101, 0, 1, null, null) { Durability = 3000, MaxDurability = 6600 });
        Tick();
        _world.Equipment[0] = _world.Equipment[0] with { Durability = 6600 };
        Tick();
        _world.Equipment[1] = _world.Equipment[1] with { Durability = 6600 }; // тот же ремонт — следующий снимок
        Tick();
        _world.Equipment[0] = _world.Equipment[0] with { Durability = 6500 };
        Tick(600);
        _world.Equipment[0] = _world.Equipment[0] with { Durability = 6600 };
        Tick();

        Assert.Equal(2, Of(StatKind.Repair).Count);
    }
}

using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.World;

/// <summary>Толкование мобов по истории снимков: застрявшая цель после отагра, «идёт к нам».</summary>
public class NpcTrackerTests
{
    private readonly FakeWorld _world = new();
    private readonly NpcTracker _tracker = new();

    private NpcInfo Next() => Assert.Single(_tracker.Track(_world.Wait(0.25).Snapshot()).Npcs);

    [Fact]
    public void StuckTargetAfterReturnIsClearedUntilMobHitsAgain()
    {
        // 1.3.6 после отагра цель не сбрасывает: видели «возвращается» — дальше та же цель застрявшая, пока моб снова не ударит
        var mob = _world.AddMob(1, "Скарабей", 10, targetWid: FakeWorld.HostWid);
        Assert.Equal(FakeWorld.HostWid, Next().TargetWid);

        _world.Replace(mob, m => m with { Returning = true, State = NpcInfo.StateMoving });
        Assert.True(Next().Returning);

        _world.Replace(mob, m => m with { Returning = false, State = 1 });
        Assert.Equal(0u, Next().TargetWid);

        _world.Replace(mob, m => m with { State = NpcInfo.StateAttacking });
        Assert.Equal(FakeWorld.HostWid, Next().TargetWid);
        _world.Replace(mob, m => m with { State = 1 });
        Assert.Equal(FakeWorld.HostWid, Next().TargetWid); // новый агр — уже не застрявшая
    }

    [Fact]
    public void OtherTargetAfterReturnIsNewAggro()
    {
        var mob = _world.AddMob(1, "Скарабей", 10, targetWid: FakeWorld.HostWid);
        _world.Replace(mob, m => m with { Returning = true });
        Next();

        _world.Replace(mob, m => m with { Returning = false, State = 1, TargetWid = FakeWorld.PetWid });

        Assert.Equal(FakeWorld.PetWid, Next().TargetWid);
    }

    [Fact]
    public void MovingMobGettingCloserIsApproaching()
    {
        var mob = _world.AddMob(1, "Волк", 20, state: NpcInfo.StateMoving);
        Assert.False(Next().Approaching);

        _world.Replace(mob, m => m with { Position = new Position(18, 0, 0) });
        Assert.True(Next().Approaching);

        _world.Replace(mob, m => m with { Position = new Position(19, 0, 0) });
        Assert.False(Next().Approaching);
    }

    [Fact]
    public void HostComingCloserIsNotMobApproaching()
    {
        // Стоящий моб, к которому бежит персонаж, — не «идёт к нам»: нужно и «идёт», и ближе
        _world.AddMob(1, "Волк", 20);
        Next();

        _world.Position = new Position(5, 0, 0);

        Assert.False(Next().Approaching);
    }

    [Fact]
    public void VanishedMobIsForgotten()
    {
        // Моб исчез (убит, респавн с тем же WID) — его прошлое расстояние не делает нового «идущим к нам»
        _world.AddMob(1, "Волк", 20, state: NpcInfo.StateMoving);
        Next();
        _world.Npcs.Clear();
        _tracker.Track(_world.Snapshot());

        _world.AddMob(1, "Волк", 15, state: NpcInfo.StateMoving);

        Assert.False(Next().Approaching);
    }
}

using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.World;

/// <summary>
/// Кто из мобов напал на перса или пета (<see cref="NpcTracker"/>): подтверждения (цель, удар, список ударов), память между
/// ударами; догадок по анимации нет. Случаи — из записей живых клиентов 05.11 (tz-refactoring/26-мобы-и-агр.md).
/// </summary>
public class NpcAggroTests
{
    private const uint PlayerWid = 0x0004_1234;

    private readonly FakeWorld _world = new();
    private readonly NpcTracker _tracker = new();

    private NpcInfo Next(double seconds = 0.25)
    {
        var snapshot = _tracker.Track(_world.Wait(seconds).Snapshot());
        return Assert.Single(snapshot.Npcs, n => n.Kind == NpcKind.Mob);
    }

    private static PlayerInfo Player(float distance) => new(2, PlayerWid, "Игрок", new Position(distance, 0, 0));

    [Fact]
    public void MobStandingBetweenHitsStaysOnUs()
    {
        // Classic: цель видна только в момент удара; между ударами моб «стоит», поле пустое — всё равно бьёт нас
        var mob = _world.AddMob(1, "Ядовитая кора", 3, targetWid: FakeWorld.HostWid, state: NpcState.Attacking);
        Assert.Equal(AggroSign.Action, Next().Aggro);

        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0 });
        var standing = Next(2);

        Assert.Equal(FakeWorld.HostWid, standing.TargetWid);
        Assert.Equal(AggroSign.Held, standing.Aggro);
    }

    [Fact]
    public void HoldExpiresWithoutConfirmations()
    {
        var mob = _world.AddMob(1, "Ядовитая кора", 3, targetWid: FakeWorld.HostWid, state: NpcState.Attacking);
        Next();
        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0 });

        var later = Next(NpcTracker.HoldFor.TotalSeconds + 1);

        Assert.Equal(0u, later.TargetWid);
        Assert.Equal(AggroSign.None, later.Aggro);
    }

    [Fact]
    public void CombatPoseDoesNotExtendHold()
    {
        // Держим только по подтверждениям: стойка без ударов дольше HoldFor — уже не наш
        var mob = _world.AddMob(1, "Бес пустоты", 3, targetWid: FakeWorld.HostWid, state: NpcState.Attacking);
        Next();
        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0, Pose = NpcPose.Fighting });

        Next(NpcTracker.HoldFor.TotalSeconds - 1);
        var later = Next(2);

        Assert.Equal(0u, later.TargetWid);
        Assert.Equal(AggroSign.None, later.Aggro);
    }

    [Fact]
    public void TargetBlinkingToNobodyKeepsAggro()
    {
        // Comeback 1.4.6: при смене пет ↔ перс цель на мгновение -1, а моб «стоит» — раньше бот писал «отстал»
        var mob = _world.AddMob(1, "Призрачный волк-воин", 3, targetWid: FakeWorld.PetWid);
        _world.SetPet(1);
        Assert.Equal(FakeWorld.PetWid, Next().TargetWid);

        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0xFFFFFFFF });
        Assert.Equal(FakeWorld.PetWid, Next().TargetWid);

        _world.Replace(mob, m => m with { State = NpcState.Attacking, TargetWid = FakeWorld.HostWid });
        Assert.Equal(FakeWorld.HostWid, Next().TargetWid);
    }

    [Fact]
    public void TargetFieldIsEnoughWhileChasing()
    {
        // Comeback: агрессивный моб бежит за убегающим персом (расстояние растёт) — цель — мы с первой четверти секунды
        var mob = _world.AddMob(1, "Царь черных гадюк", 4, targetWid: FakeWorld.HostWid, state: NpcState.Moving);
        Next();
        _world.Replace(mob, m => m with { Position = new Position(9, 0, 0) });

        var chasing = Next();

        Assert.Equal(FakeWorld.HostWid, chasing.TargetWid);
        Assert.Equal(AggroSign.Target, chasing.Aggro);
    }

    [Fact]
    public void HitInAttackListConfirms()
    {
        // Ни поля цели, ни удара в состоянии — но его удар по нам в списке ударов игры
        _world.AddMob(1, "Голем-страж", 3);
        _world.Players.Add(Player(20));
        _world.Attacks.Add(new AttackEvent(0x500, 300, 1, 0, [new AttackHit(FakeWorld.HostWid, 80, 0)]));

        var mob = Next();

        Assert.Equal(FakeWorld.HostWid, mob.TargetWid);
        Assert.Equal(AggroSign.Hit, mob.Aggro);
    }

    [Fact]
    public void MissIsAlsoAHit()
    {
        _world.AddMob(1, "Голем-страж", 3);
        _world.Players.Add(Player(20));
        _world.Attacks.Add(new AttackEvent(0x500, 300, 1, 0, [new AttackHit(FakeWorld.HostWid, 0, 0)]));

        Assert.Equal(AggroSign.Hit, Next().Aggro);
    }

    [Fact]
    public void HittingSomeoneElseDropsHold()
    {
        var mob = _world.AddMob(1, "Голем-страж", 3, targetWid: FakeWorld.HostWid);
        _world.Players.Add(Player(5));
        Next();
        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0 });
        _world.Attacks.Add(new AttackEvent(0x500, 300, 1, 0, [new AttackHit(PlayerWid, 120, 0)]));

        Assert.Equal(AggroSign.None, Next().Aggro);
    }

    [Fact]
    public void TargetingAnotherPlayerDropsHold()
    {
        var mob = _world.AddMob(1, "Голем-страж", 3, targetWid: FakeWorld.HostWid);
        _world.Players.Add(Player(5));
        Next();
        _world.Replace(mob, m => m with { TargetWid = PlayerWid });

        var switched = Next();

        Assert.Equal(PlayerWid, switched.TargetWid);
        Assert.Equal(AggroSign.None, switched.Aggro);
    }

    [Fact]
    public void UnknownTargetDoesNotDropHold()
    {
        // Мусор в поле (WID, которого нет вокруг) — не «сменил цель»
        var mob = _world.AddMob(1, "Голем-страж", 3, targetWid: FakeWorld.HostWid);
        Next();
        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0x016D016C });

        Assert.Equal(FakeWorld.HostWid, Next().TargetWid);
    }

    [Fact]
    public void ReturningAndDeadDropAggro()
    {
        var mob = _world.AddMob(1, "Злой бык-рыболов", 3, targetWid: FakeWorld.HostWid);
        Next();

        _world.Replace(mob, m => m with { Returning = true, State = NpcState.Standing });
        Assert.Equal(AggroSign.None, Next().Aggro);

        _world.Replace(mob, m => m with { Returning = false, State = NpcState.Dead });
        Assert.Equal(AggroSign.None, Next().Aggro);
    }

    [Fact]
    public void StuckTargetAfterReturnIsNotAggro()
    {
        // 1.3.6: после отагра наш WID висит в цели ~30 с — это не нападение
        var mob = _world.AddMob(1, "Злой бык-рыболов", 3, targetWid: FakeWorld.HostWid);
        Next();
        _world.Replace(mob, m => m with { Returning = true, State = NpcState.Standing });
        Next();

        _world.Replace(mob, m => m with { Returning = false });
        var stuck = Next(20);

        Assert.Equal(0u, stuck.TargetWid);
        Assert.Equal(AggroSign.None, stuck.Aggro);
    }

    [Theory]
    [InlineData(NpcPose.Fighting)]
    [InlineData(NpcPose.Running)]
    public void CombatPoseWithoutHitIsNotAggro(NpcPose pose)
    {
        // Classic: моб в стойке или бежит, вокруг никого, но цели и удара нет — не считаем напавшим, пока не ударит
        var mob = _world.AddMob(1, "Бес пустоты", 15, state: NpcState.Moving);
        _world.Replace(mob, m => m with { Pose = pose });

        var seen = Next();

        Assert.Equal(0u, seen.TargetWid);
        Assert.Equal(AggroSign.None, seen.Aggro);
    }




    [Fact]
    public void RunningMobWithTargetOnUsIsAggro()
    {
        // Comeback: поле цели есть — бегущий к нам моб с нашей целью уже напал
        var mob = _world.AddMob(1, "Бес пустоты", 15, targetWid: FakeWorld.HostWid, state: NpcState.Moving);
        _world.Replace(mob, m => m with { Pose = NpcPose.Running });

        var seen = Next();

        Assert.Equal(FakeWorld.HostWid, seen.TargetWid);
        Assert.Equal(AggroSign.Target, seen.Aggro);
    }

    [Fact]
    public void WalkingTowardsUsIsNotAggro()
    {
        // Носорог 2,6 с шёл в сторону перса шагом — просто гулял
        var mob = _world.AddMob(1, "Вопящий носорог", 15, state: NpcState.Moving);
        _world.Replace(mob, m => m with { Pose = NpcPose.Walking });
        Next();
        _world.Replace(mob, m => m with { Position = new Position(13, 0, 0) });

        Assert.Equal(AggroSign.None, Next().Aggro);
    }







    [Fact]
    public void RecalledPetIsNoLongerTheVictim()
    {
        var mob = _world.AddMob(1, "Голем-страж", 3, targetWid: FakeWorld.PetWid);
        _world.SetPet(1);
        _world.Players.Add(Player(30));
        Next();

        _world.SetPet(1, summoned: false);
        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0 });

        Assert.Equal(AggroSign.None, Next().Aggro);
    }

    [Fact]
    public void RespawnedMobStartsClean()
    {
        // WID после респавна тот же — память о прошлом бое снимает смерть
        var mob = _world.AddMob(1, "Ядовитая кора", 3, targetWid: FakeWorld.HostWid);
        _world.Players.Add(Player(30));
        Next();
        _world.Replace(mob, m => m with { State = NpcState.Dead });
        Next();

        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0, Position = new Position(20, 0, 0) });

        Assert.Equal(AggroSign.None, Next().Aggro);
    }

    [Fact]
    public void ReaderSnapshotIsNotInterpreted()
    {
        // Без толкования (прямо из читателя) — признака нет; бой и тесты поведения смотрят только на цель
        _world.AddMob(1, "Голем-страж", 3, targetWid: FakeWorld.HostWid);

        Assert.Equal(AggroSign.None, Assert.Single(_world.Snapshot().Npcs).Aggro);
    }
}

using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.World;

/// <summary>
/// Кто из мобов напал на перса или пета (<see cref="NpcTracker"/>): подтверждения (цель, удар, список ударов), память между
/// ударами и догадка по анимации. Случаи — из записей живых клиентов 05.11 (tz-refactoring/26-мобы-и-агр.md).
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
    public void CombatPoseKeepsHold()
    {
        // Моб в боевой стойке — ещё в бою, держим дольше HoldFor
        var mob = _world.AddMob(1, "Бес пустоты", 3, targetWid: FakeWorld.HostWid, state: NpcState.Attacking);
        Next();
        _world.Replace(mob, m => m with { State = NpcState.Standing, TargetWid = 0, Pose = NpcPose.Fighting });
        _world.Players.Add(Player(30)); // не один — догадкой не подменить

        Next(NpcTracker.HoldFor.TotalSeconds - 1);
        var later = Next(NpcTracker.HoldFor.TotalSeconds - 1);

        Assert.Equal(FakeWorld.HostWid, later.TargetWid);
        Assert.Equal(AggroSign.Held, later.Aggro);
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

    [Fact]
    public void CombatPoseWhenAloneIsAggro()
    {
        // Classic: цели у бегущего моба нет, но он в боевой стойке, а вокруг никого — значит, на нас
        var mob = _world.AddMob(1, "Бес пустоты", 15);
        _world.Replace(mob, m => m with { Pose = NpcPose.Fighting });

        var seen = Next();

        Assert.Equal(FakeWorld.HostWid, seen.TargetWid);
        Assert.Equal(AggroSign.Pose, seen.Aggro);
    }

    [Fact]
    public void RunningWhenAloneIsAggro()
    {
        var mob = _world.AddMob(1, "Бес пустоты", 15, state: NpcState.Moving);
        _world.Replace(mob, m => m with { Pose = NpcPose.Running });

        Assert.Equal(AggroSign.Pose, Next().Aggro);
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
    public void CombatPoseWithPlayersAroundIsNotAggro()
    {
        var mob = _world.AddMob(1, "Бес пустоты", 15);
        _world.Replace(mob, m => m with { Pose = NpcPose.Fighting });
        _world.Players.Add(Player(25));

        Assert.Equal(AggroSign.None, Next().Aggro);
    }

    [Fact]
    public void CombatPoseWithForeignPetAroundIsNotAggro()
    {
        var mob = _world.AddMob(1, "Бес пустоты", 15);
        _world.Replace(mob, m => m with { Pose = NpcPose.Fighting });
        _world.Npcs.Add(new NpcInfo(7, 0x80119999, NpcKind.Pet, NpcState.Standing, 0, new Position(30, 0, 0), "Чужой пет", 0));

        Assert.Equal(AggroSign.None, Next().Aggro);
    }

    [Fact]
    public void FarCombatPoseIsNotAggro()
    {
        var mob = _world.AddMob(1, "Бес пустоты", NpcTracker.PoseRadius + 5);
        _world.Replace(mob, m => m with { Pose = NpcPose.Fighting });

        Assert.Equal(AggroSign.None, Next().Aggro);
    }

    [Fact]
    public void CombatPoseGuessPicksNearestOfUs()
    {
        // Пет в 2 м от перса, моб за петом — догадка: на пета
        _world.SetPet(1);
        var mob = _world.AddMob(1, "Бес пустоты", 5);
        _world.Replace(mob, m => m with { Pose = NpcPose.Fighting });

        Assert.Equal(FakeWorld.PetWid, Next().TargetWid);
    }

    [Fact]
    public void CombatPoseGuessFadesWhenMobCalms()
    {
        var mob = _world.AddMob(1, "Бес пустоты", 15);
        _world.Replace(mob, m => m with { Pose = NpcPose.Fighting });
        Next();

        _world.Replace(mob, m => m with { Pose = NpcPose.Standing });
        Assert.Equal(AggroSign.Pose, Next(0.5).Aggro); // анимация мигает — догадку держим недолго
        Assert.Equal(AggroSign.None, Next(NpcTracker.PoseGrace.TotalSeconds).Aggro);
    }

    [Fact]
    public void NoPoseGuessAfterReturnUntilMobCalms()
    {
        // Отагрился и бежит домой — его бег не догадка о нападении, пока не встанет или не пойдёт шагом
        var mob = _world.AddMob(1, "Бес пустоты", 15, targetWid: FakeWorld.HostWid);
        Next();
        _world.Replace(mob, m => m with { Returning = true, State = NpcState.Moving, Pose = NpcPose.Running, TargetWid = 0 });
        Next();

        _world.Replace(mob, m => m with { Returning = false });
        Assert.Equal(AggroSign.None, Next().Aggro);

        _world.Replace(mob, m => m with { State = NpcState.Standing, Pose = NpcPose.Standing });
        Next();
        _world.Replace(mob, m => m with { Pose = NpcPose.Fighting });
        Assert.Equal(AggroSign.Pose, Next().Aggro);
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

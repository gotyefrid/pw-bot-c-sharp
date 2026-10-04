using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BotCH.Core.Actions;
using BotCH.Core.Calls;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Actions;

/// <summary>Подтверждение действий по последовательности подставных снимков.</summary>
public class ActionRunnerTests
{
    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly ActionRunner _runner;

    public ActionRunnerTests() => _runner = new ActionRunner(_actions, NullLogger.Instance);

    private ActionOutcome Single(IReadOnlyList<ActionOutcome> outcomes) => Assert.Single(outcomes);

    [Fact]
    public void SkillConfirmedByCooldown()
    {
        _world.AddSkill(299);
        var mob = _world.AddMob(0x80104298, "Волк", 20);
        _world.TargetWid = mob.Wid;

        Assert.True(_runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot()).Sent);
        Assert.Empty(_runner.Update(_world.Wait(0.3).Snapshot()));
        _world.SetCooldown(299, 2500);
        var outcome = Single(_runner.Update(_world.Wait(0.3).Snapshot()));

        Assert.Equal(ActionStatus.Confirmed, outcome.Status);
        Assert.Equal(["apply 299 0"], _actions.Calls);
    }

    [Fact]
    public void SkillNotResentWhileApproaching()
    {
        // Перс идёт к цели 3 снимка — скилл не отправляется повторно
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(0x80104298, "Волк", 30).Wid;

        _runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot());
        for (var i = 0; i < 3; i++)
        {
            _runner.Update(_world.Wait(0.3).Snapshot());
            Assert.Equal(SubmitStatus.AlreadyPending, _runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot()).Status);
        }

        Assert.Single(_actions.Calls);
    }

    [Fact]
    public void SkillTimesOut()
    {
        _world.AddSkill(299);

        _runner.Submit(new SkillAction(299, 0x80104298, approach: false), _world.Snapshot());
        Assert.Empty(_runner.Update(_world.Wait(7).Snapshot()));
        var outcome = Single(_runner.Update(_world.Wait(1.5).Snapshot()));

        Assert.Equal(ActionStatus.Timeout, outcome.Status);
        Assert.False(_runner.IsPending("скилл 299"));
    }

    [Fact]
    public void SkillOnChangedTargetIsRejected()
    {
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(1, "Волк", 20).Wid;

        _runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot());
        _world.TargetWid = _world.AddMob(2, "Медведь", 25).Wid;

        Assert.Equal(ActionStatus.Rejected, Single(_runner.Update(_world.Wait(0.3).Snapshot())).Status);
    }

    [Fact]
    public void SkillOnDeadTargetIsCancelledNotRejected()
    {
        // Скилл заказан по живому, моб умер, бот снял цель — это не отказ игры
        _world.AddSkill(299);
        var mob = _world.AddMob(1, "Волк", 20);
        _world.TargetWid = mob.Wid;

        _runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot());
        _world.Replace(mob, m => m with { State = NpcInfo.StateDead });
        _world.TargetWid = 0;

        var outcome = Single(_runner.Update(_world.Wait(0.3).Snapshot()));
        Assert.Equal(ActionStatus.Cancelled, outcome.Status);
        Assert.Contains("цель умерла", outcome.ToString());
    }

    [Fact]
    public void GatherKnockedDownIsRejectedAtOnce()
    {
        // Копали камень 8 с, моб сбил на 1,8 с — не ждём 30 с
        var stone = new GroundItem(1, 0xC0100AD9, 3079, GroundItemKind.Resource, default, 2, "Залежи камня");
        _world.Ground.Add(stone);
        _world.Gather = new GatherProgress(false, 8000, 8000);
        _runner.Submit(new GatherAction(stone), _world.Snapshot());

        _world.Gather = new GatherProgress(true, 200, 8000);
        Assert.Empty(_runner.Update(_world.Wait(0.2).Snapshot()));
        _world.Gather = new GatherProgress(false, 1800, 8000);

        var outcome = Single(_runner.Update(_world.Wait(1.6).Snapshot()));
        Assert.Equal(ActionStatus.Rejected, outcome.Status);
        Assert.Contains("сбили: 1,8 из 8 с", outcome.ToString());
    }

    [Fact]
    public void GatherFinishedWaitsForLoot()
    {
        // Полоска дошла до конца, ресурс ещё виден пару кадров — это не срыв
        var ore = new GroundItem(1, 0xC0100E4B, 3079, GroundItemKind.Resource, default, 2, "Железная руда");
        _world.Ground.Add(ore);
        _runner.Submit(new GatherAction(ore), _world.Snapshot());
        _world.Gather = new GatherProgress(true, 2500, 5000);
        Assert.Empty(_runner.Update(_world.Wait(2.5).Snapshot()));

        _world.Gather = new GatherProgress(false, 5000, 5000);
        Assert.Empty(_runner.Update(_world.Wait(2.5).Snapshot()));
        _world.Ground.Clear();

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(0.2).Snapshot())).Status);
    }

    [Fact]
    public void GatherNotStartedWhileStandingIsRejected()
    {
        // Подошли, а полоски нет (нет кирки) — 4 с стоим и бросаем
        var ore = new GroundItem(1, 0xC0100E4B, 3079, GroundItemKind.Resource, default, 2, "Железная руда");
        _world.Ground.Add(ore);
        _world.Gather = new GatherProgress(false, 0, 0);
        _runner.Submit(new GatherAction(ore), _world.Snapshot());

        Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        _world.Position = new Position(1, 0, 0); // ещё бежим
        Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        Assert.Empty(_runner.Update(_world.Wait(3).Snapshot()));

        var outcome = Single(_runner.Update(_world.Wait(1.2).Snapshot()));
        Assert.Equal(ActionStatus.Rejected, outcome.Status);
        Assert.Contains("нет инструмента", outcome.ToString());
    }

    [Fact]
    public void SecondBodyActionWaitsWhileFirstPending()
    {
        // Бег к мобу ещё идёт — скилл «как кнопкой» не отправляется поверх (оба — «работы» персонажа)
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(1, "Волк", 20).Wid;
        var move = new MoveAction(new Position(10, 0, 0));

        Assert.True(_runner.Submit(move, _world.Snapshot()).Sent);
        var skill = _runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot());

        Assert.Equal(SubmitStatus.Busy, skill.Status);
        Assert.Equal(move.Name, skill.Busy);
        Assert.Equal(["move (10,0; 0,0; h 0,0)"], _actions.Calls);
    }

    // ── Полёт ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TakeOffConfirmedWhenInAir()
    {
        Assert.True(_runner.Submit(new FlyAction(up: true), _world.Snapshot()).Sent);
        Assert.Empty(_runner.Update(_world.Wait(0.3).Snapshot()));
        _world.Flying = true;

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(0.3).Snapshot())).Status);
        Assert.Equal(["fly-toggle"], _actions.Calls);
    }

    [Theory]
    [InlineData(true, true)]    // уже в воздухе — «взлететь» нажало бы «сесть»
    [InlineData(false, false)]  // уже на земле
    [InlineData(null, true)]    // не знаем, где перс, — не жмём переключатель вслепую
    public void FlyToggleNotPressedWhenAlreadyThere(bool? flying, bool up)
    {
        _world.Flying = flying;

        Assert.False(_runner.Submit(new FlyAction(up), _world.Snapshot()).Sent);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void FlyToPointOnlyInAirAndConfirmedAtHeight()
    {
        var point = new Position(0, 20, 0);
        Assert.False(_runner.Submit(new MoveAction(point, fly: true), _world.Snapshot()).Sent);

        _world.Flying = true;
        Assert.True(_runner.Submit(new MoveAction(point, fly: true), _world.Snapshot()).Sent);
        // Поднимаемся на месте — это движение, а не «стоим»
        for (var h = 3; h <= 15; h += 3)
        {
            _world.Position = new Position(0, h, 0);
            Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        }

        _world.Position = point;
        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(1).Snapshot())).Status);
        Assert.Equal([$"fly {point}"], _actions.Calls);
    }

    // ── Важность: кто кого перебивает ──────────────────────────────────────────

    private SkillAction UrgentHeal() => new(330, FakeWorld.PetWid, approach: true, "лечение пета") { Priority = ActionPriority.Urgent };

    [Fact]
    public void UrgentDropsNormalPendingAndGoesAtOnce()
    {
        _world.AddSkill(330);
        var move = new MoveAction(new Position(30, 0, 0));
        _runner.Submit(move, _world.Snapshot());

        var heal = _runner.Submit(UrgentHeal(), _world.Snapshot());

        Assert.Equal(SubmitStatus.Sent, heal.Status);
        Assert.Equal([$"move {move.Point}", $"apply 330 {FakeWorld.PetWid:X}"], _actions.Calls);
        Assert.IsType<SkillAction>(_runner.BodyAction);
    }

    [Fact]
    public void NormalWaitsForUrgent()
    {
        _world.AddSkill(330);
        _runner.Submit(UrgentHeal(), _world.Snapshot());

        Assert.Equal(SubmitStatus.Busy, _runner.Submit(new MoveAction(new Position(30, 0, 0)), _world.Snapshot()).Status);
    }

    [Fact]
    public void UrgentBreaksOtherKnownCastButNotItsOwn()
    {
        _world.AddSkill(330);
        _world.Casting = true;

        _world.CastingSkillId = 330;
        Assert.Equal("кастуется этот же скилл", _runner.Submit(UrgentHeal(), _world.Snapshot()).Busy);
        Assert.Empty(_actions.Calls);

        _world.CastingSkillId = 299;
        Assert.Equal(SubmitStatus.Busy, _runner.Submit(UrgentHeal(), _world.Snapshot()).Status);
        Assert.Equal(["cancel"], _actions.Calls);
    }

    [Fact]
    public void UnknownCastIsNeverBroken()
    {
        _world.AddSkill(330);
        _world.Casting = true;
        _world.CastingSkillId = null;

        Assert.Equal("персонаж кастует", _runner.Submit(UrgentHeal(), _world.Snapshot()).Busy);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void NormalBreaksDiggingButNotCast()
    {
        // Копание — фон: бой его прерывает; чужой каст обычное действие не трогает
        var ore = new GroundItem(1, 0xC0100E4B, 3079, GroundItemKind.Resource, default, 2, "Железная руда");
        _world.Ground.Add(ore);
        _runner.Submit(new GatherAction(ore), _world.Snapshot());
        _world.Gather = new GatherProgress(true, 1000, 5000);

        Assert.Equal("прерываю копание", _runner.Submit(new NormalAttackAction(), _world.Snapshot()).Busy);
        Assert.Equal(["gather C0100E4B", "cancel"], _actions.Calls);
        Assert.Null(_runner.Pending.OfType<GatherAction>().FirstOrDefault());

        _world.Gather = new GatherProgress(false, 1200, 5000);
        _world.Casting = true;
        _world.CastingSkillId = 299;
        Assert.Equal(SubmitStatus.Busy, _runner.Submit(new MoveAction(new Position(5, 0, 0)), _world.Snapshot()).Status);
        Assert.Equal(2, _actions.Calls.Count);
    }

    [Fact]
    public void BodyActionWaitsWhileCasting()
    {
        // Новое действие с телом сбило бы каст; банка — нет
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(1, "Волк", 20).Wid;
        var potion = _world.AddPotion(2, 8618, 25, hp: 80);
        _world.Casting = true;

        var skill = _runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot());

        Assert.Equal(SubmitStatus.Busy, skill.Status);
        Assert.Equal("персонаж кастует", skill.Busy);
        Assert.True(_runner.Submit(new UseItemAction(potion, ItemUse.Potion), _world.Snapshot()).Sent);
    }

    [Fact]
    public void FreeActionsGoAlongsideBody()
    {
        // Банка и приказ пету тело не занимают — идут, пока бежим
        var potion = _world.AddPotion(2, 8618, 25, hp: 80);
        _world.TargetWid = _world.AddMob(1, "Волк", 20).Wid;
        _world.SetPet(1);
        _runner.Submit(new MoveAction(new Position(10, 0, 0)), _world.Snapshot());

        Assert.True(_runner.Submit(new UseItemAction(potion, ItemUse.Potion), _world.Snapshot()).Sent);
        Assert.True(_runner.Submit(new PetAttackAction(1), _world.Snapshot()).Sent);
    }

    [Fact]
    public void BodyFreesWhenActionCompletes()
    {
        _world.AddSkill(299);
        _world.TargetWid = _world.AddMob(1, "Волк", 20).Wid;
        _runner.Submit(new MoveAction(new Position(10, 0, 0)), _world.Snapshot());

        _world.Position = new Position(10, 0, 0);
        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(0.3).Snapshot())).Status);

        Assert.Null(_runner.BodyAction);
        Assert.True(_runner.Submit(new SkillAction(299, 0, approach: true), _world.Snapshot()).Sent);
    }

    [Fact]
    public void SkillOnCooldownIsNotSent()
    {
        _world.AddSkill(299, cooldownLeftMs: 1500);

        var result = _runner.Submit(new SkillAction(299, 0, approach: false), _world.Snapshot());

        Assert.Equal(SubmitStatus.Failed, result.Status);
        Assert.Contains("перезарядка", result.Outcome!.Details);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void PotionConfirmedWhenStackShrinks()
    {
        var potion = _world.AddPotion(2, 8618, 25, hp: 80);

        _runner.Submit(new UseItemAction(potion, ItemUse.Potion), _world.Snapshot());
        _world.Consume(2);
        var outcome = Single(_runner.Update(_world.Wait(0.3).Snapshot()));

        Assert.Equal(ActionStatus.Confirmed, outcome.Status);
        Assert.Contains("25 → 24", outcome.Details);
    }

    [Fact]
    public void PotionNotAcceptedIsRejected()
    {
        var potion = _world.AddPotion(2, 8618, 25, hp: 80);

        _runner.Submit(new UseItemAction(potion, ItemUse.Potion), _world.Snapshot());
        _runner.Update(_world.Wait(1).Snapshot());
        var outcome = Single(_runner.Update(_world.Wait(2.5).Snapshot()));

        Assert.Equal(ActionStatus.Rejected, outcome.Status);
    }

    [Fact]
    public void LastPotionGoneIsConfirmed()
    {
        var potion = _world.AddPotion(3, 8617, 1, hp: 30);

        _runner.Submit(new UseItemAction(potion, ItemUse.Potion), _world.Snapshot());
        _world.Bag.Clear();

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(0.3).Snapshot())).Status);
    }

    [Fact]
    public void PickupConfirmedWhenItemDisappears()
    {
        var coin = new GroundItem(1, 0xC0126480, 3044, GroundItemKind.Money, default, 6, "Монета");
        _world.Ground.Add(coin);

        _runner.Submit(new PickupAction(coin, approach: true), _world.Snapshot());
        Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        _world.Ground.Clear();

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(1).Snapshot())).Status);
        Assert.Equal(["pickup-approach C0126480"], _actions.Calls);
    }

    [Fact]
    public void PickupGivesUpAfter3sStandingNextToItem()
    {
        var fur = new GroundItem(1, 0xC01040F7, 830, GroundItemKind.Item, new Position(2, 0, 0), 2, "Мех животных");
        _world.Ground.Add(fur);

        _runner.Submit(new PickupAction(fur, approach: true), _world.Snapshot());
        Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        var outcome = Single(_runner.Update(_world.Wait(2.5).Snapshot()));

        Assert.Equal(ActionStatus.Rejected, outcome.Status);
        Assert.Contains("не отдаёт", outcome.Details);
    }

    [Fact]
    public void PickupWaitsWhileWalkingToItem()
    {
        var fur = new GroundItem(1, 0xC01040F7, 830, GroundItemKind.Item, new Position(8, 0, 0), 8, "Мех животных");
        _world.Ground.Add(fur);

        _runner.Submit(new PickupAction(fur, approach: true), _world.Snapshot());
        Assert.Empty(_runner.Update(_world.Wait(4).Snapshot())); // ещё идём — 8 м
        _world.Position = new Position(6, 0, 0);
        Assert.Empty(_runner.Update(_world.Wait(1).Snapshot())); // подошли
        _world.Ground.Clear();

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(0.5).Snapshot())).Status);
    }

    [Fact]
    public void ResourceIsNeverPickedUp()
    {
        var ore = new GroundItem(1, 0xC0100AD9, 3089, GroundItemKind.Resource, default, 5, "Шахта крупного угля");

        Assert.Equal(SubmitStatus.Failed, _runner.Submit(new PickupAction(ore, approach: true), _world.Snapshot()).Status);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void FarPacketPickupIsRefused()
    {
        var far = new GroundItem(1, 0xC0000001, 3044, GroundItemKind.Money, default, 15, "Монета");

        Assert.Equal(SubmitStatus.Failed, _runner.Submit(new PickupAction(far, approach: false), _world.Snapshot()).Status);
    }

    [Fact]
    public void SelectTargetConfirmed()
    {
        var mob = _world.AddMob(0x80104298, "Волк", 10);

        _runner.Submit(new SelectTargetAction(mob), _world.Snapshot());
        _world.TargetWid = mob.Wid;

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(0.2).Snapshot())).Status);
    }

    [Fact]
    public void AttackConfirmedWhenMobTurnsOnHost()
    {
        var mob = _world.AddMob(0x80104298, "Волк", 10, hp: 500);
        _world.TargetWid = mob.Wid;

        _runner.Submit(new NormalAttackAction(), _world.Snapshot());
        _world.Replace(mob, m => m with { TargetWid = FakeWorld.HostWid });

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(1).Snapshot())).Status);
    }

    [Fact]
    public void AttackConfirmedByHpWhenMobBeatsPet()
    {
        var mob = _world.AddMob(0x80104298, "Волк", 10, targetWid: FakeWorld.PetWid, hp: 500);
        _world.TargetWid = mob.Wid;

        _runner.Submit(new NormalAttackAction(), _world.Snapshot());
        _world.Replace(mob, m => m with { Hp = 470 });

        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(1).Snapshot())).Status);
    }

    [Fact]
    public void MoveConfirmedNearPointAndRejectedWhenStuck()
    {
        var point = new Position(20, 0, 0);
        _runner.Submit(new MoveAction(point), _world.Snapshot());
        _world.Position = new Position(19, 0, 0);
        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(4).Snapshot())).Status);

        _world.Position = new Position(0, 0, 0); // нас отбросило: бежим, но медленно — не успеваем за время на дорогу
        _runner.Submit(new MoveAction(point), _world.Snapshot());
        for (var x = 1; x <= 10; x++)
        {
            _world.Position = new Position(x * 0.5f, 0, 0);
            Assert.Empty(_runner.Update(_world.Wait(1).Snapshot()));
        }
        Assert.Equal(ActionStatus.Rejected, Single(_runner.Update(_world.Wait(10).Snapshot())).Status);
    }

    [Fact]
    public void MoveRejectedSoonWhenStandingShortOfPoint()
    {
        // Бег на 25 м (времени на дорогу 15 с), а перс встал в 7 м от точки — отказ через 2.5 с стояния, не через 15
        var point = new Position(25, 0, 0);
        _runner.Submit(new MoveAction(point), _world.Snapshot());
        _world.Position = new Position(10, 0, 0);
        Assert.Empty(_runner.Update(_world.Wait(2).Snapshot()));
        _world.Position = new Position(18, 0, 0);
        Assert.Empty(_runner.Update(_world.Wait(2).Snapshot()));
        Assert.Empty(_runner.Update(_world.Wait(2).Snapshot()));

        var outcome = Single(_runner.Update(_world.Wait(1).Snapshot()));
        Assert.Equal(ActionStatus.Rejected, outcome.Status);
        Assert.StartsWith("стоим", outcome.Details);
    }

    [Fact]
    public void SummonAndReviveFollowPetState()
    {
        _world.SetPet(1, hpRatio: 0, summoned: false);
        _world.AddSkill(329);

        Assert.Equal(SubmitStatus.Failed, _runner.Submit(new SummonPetAction(1), _world.Snapshot()).Status); // мёртв

        Assert.True(_runner.Submit(new RevivePetAction(1, 329), _world.Snapshot()).Sent);
        Assert.Empty(_runner.Update(_world.Wait(5).Snapshot()));
        _world.SetPet(1, hpRatio: 0.1f, summoned: false);
        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(7).Snapshot())).Status);

        Assert.True(_runner.Submit(new SummonPetAction(1), _world.Snapshot()).Sent);
        _world.SetPet(1, hpRatio: 0.1f, summoned: true);
        Assert.Equal(ActionStatus.Confirmed, Single(_runner.Update(_world.Wait(3.5).Snapshot())).Status);

        Assert.Equal(["cast 329 0", "summon 1"], _actions.Calls);
    }

    [Fact]
    public void NoPetMeansPetActionsAreNotSent()
    {
        // Не друид: Pet = null — никаких вызовов, просто «не отправлено»
        Assert.Equal(SubmitStatus.Failed, _runner.Submit(new SummonPetAction(1), _world.Snapshot()).Status);
        Assert.Equal(SubmitStatus.Failed, _runner.Submit(new PetAttackAction(1), _world.Snapshot()).Status);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void FailedCallIsReportedAndNotPending()
    {
        _actions.Result = CallResult.Refused("selectTarget: по адресу не те байты");
        var outcomes = new List<ActionOutcome>();
        _runner.Completed += outcomes.Add;

        var result = _runner.Submit(new SelectTargetAction(_world.AddMob(1, "Волк", 5)), _world.Snapshot());

        Assert.Equal(SubmitStatus.Failed, result.Status);
        Assert.Contains("не те байты", Assert.Single(outcomes).Details);
        Assert.Empty(_runner.Pending);
    }

    [Fact]
    public void ClearForgetsPending()
    {
        _world.AddSkill(299);
        _runner.Submit(new SkillAction(299, 0, approach: false), _world.Snapshot());

        _runner.Clear();

        Assert.True(_runner.Submit(new SkillAction(299, 0, approach: false), _world.Snapshot()).Sent);
    }

    [Fact]
    public void CallsNeverOverlap()
    {
        // Два потока отправляют действия одновременно — вызовы в игре всё равно идут по одному
        var slow = new SlowActions();
        var runner = new ActionRunner(slow, NullLogger.Instance);
        var snapshot = _world.Snapshot();
        var mobs = Enumerable.Range(1, 20).Select(i => new NpcInfo(0, (uint)i, NpcKind.Mob, 1, 0, default, 1, "м", 0)).ToList();

        Parallel.ForEach(mobs, mob =>
        {
            runner.Submit(new SelectTargetAction(mob), snapshot);
            runner.Clear();
        });

        Assert.Equal(1, slow.MaxParallel);
    }

    private sealed class SlowActions : IGameActions
    {
        private int _active;
        public int MaxParallel { get; private set; }
        public string Mode => "тест";
        public Capabilities Capabilities => Capabilities.All;

        private CallResult Slow()
        {
            var now = Interlocked.Increment(ref _active);
            MaxParallel = Math.Max(MaxParallel, now);
            Thread.Sleep(5);
            Interlocked.Decrement(ref _active);
            return CallResult.Done;
        }

        public CallResult SelectTarget(uint wid) => Slow();
        public CallResult Unselect() => Slow();
        public CallResult NormalAttack() => Slow();
        public CallResult CastSkill(int skillId, uint targetWid) => Slow();
        public CallResult ApplySkill(HostState host, int skillId, uint targetWid) => Slow();
        public CallResult PetAttack(uint targetWid) => Slow();
        public CallResult CancelAction() => Slow();
        public CallResult Pickup(GroundItem item) => Slow();
        public CallResult PickupObject(HostState host, GroundItem item) => Slow();
        public CallResult UseItem(InventoryItem item) => Slow();
        public CallResult SummonPet(int cage) => Slow();
        public CallResult RecallPet() => Slow();
        public CallResult MoveTo(HostState host, Position point, bool smart) => Slow();
        public CallResult Gather(HostState host, GroundItem resource) => Slow();
        public CallResult ToggleFly(HostState host) => Slow();
        public CallResult FlyTo(HostState host, Position point) => Slow();
    }
}

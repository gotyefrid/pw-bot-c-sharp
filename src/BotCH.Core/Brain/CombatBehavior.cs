using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Бой: поиск цели → бой → (лут — <see cref="LootBehavior"/>) → поиск. Как старый Bot.cs, но по снимкам, без Sleep. Что сейчас —
/// в общем <see cref="FightState"/>: его читают пет, копание и возврат.
/// <list type="bullet">
/// <item>Поиск: самый опасный моб (<see cref="Threat"/>: бьёт меня → бьёт меня или пета; белый список не важен) →
/// текущая цель, если подходит → ближайший разрешённый.</item>
/// <item>Бой: другой моб опаснее текущего — переходим на него. Приказ пету; «подойти ближе» — сначала подходим, потом бьём;
/// скилл «как кнопкой»; меч раз в ~5 с. Через N с (120) моба бросаем. Убит — лут (ход переходит к <see cref="LootBehavior"/>
/// на том же снимке).</item>
/// <item>Лут: бой важнее — напали во время лута, лут бросаем и бьём напавшего.</item>
/// </list>
/// </summary>
/// <param name="defendOnly">Только защита (обход ресурсов): сами мобов не ищем, бьём того, кто напал на перса или пета, —
/// всегда, что бы ни стояло в «сначала тех, кто бьёт меня» (это настройка фарма).</param>
public sealed class CombatBehavior(bool defendOnly = false) : IBehavior
{
    private static readonly TimeSpan SwordPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PetOrderPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TargetLostGrace = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan GiveUpFor = TimeSpan.FromSeconds(60);
    // Подход: моб ушёл от точки, к которой бежим, дальше этого — бежим к его новому месту (не чаще RetargetPeriod)
    private const float RetargetDistance = 3f;
    // Подход: «дошли» — ближе этого к точке; точку берём ещё на 0.5 м ближе к мобу, чтобы и в худшем случае оказаться
    // ближе нужного (раньше вставали на 0.5 м дальше и делали второй короткий подход)
    private const float ApproachTolerance = 1.5f;
    private const float ApproachMargin = ApproachTolerance + 0.5f;
    private static readonly TimeSpan RetargetPeriod = TimeSpan.FromSeconds(1);

    private readonly Dictionary<uint, DateTime> _gaveUp = [];

    private DateTime _fightStarted;
    private DateTime? _targetLostAt;
    private DateTime _lastSword = DateTime.MinValue;
    private DateTime _lastPetOrder = DateTime.MinValue;
    private DateTime _lastApproach;

    public string Name => "бой";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        var fight = c.Fight;
        if (!defendOnly && !c.Settings.Target.KillMobs)
        {
            // Выключили посреди боя или лута — бросаем: иначе бой так и висит «дерусь», а копание ждёт его конца
            if (fight.Busy)
            {
                c.Log.Info($"«Бить мобов» выключено — бросаю {(fight.Phase == FightPhase.Fighting ? "бой: " + fight.MobName : "лут")}");
                fight.Abort();
            }
            Status = "бой выключен";
            return false;
        }

        return fight.Phase switch
        {
            FightPhase.Fighting => Fight(c),
            FightPhase.Looting => AttackedWhileLooting(c),
            _ => Search(c),
        };
    }

    // ── Поиск ────────────────────────────────────────────────────────────────

    private bool Search(BrainContext c)
    {
        var w = c.World;
        var target = c.Settings.Target;
        foreach (var expired in _gaveUp.Where(g => g.Value <= c.Now).Select(g => g.Key).ToList())
            _gaveUp.Remove(expired);

        // Порядок: самый опасный (бьёт меня → бьёт меня или пета) → текущая цель → ближайший разрешённый
        var aggressor = MostDangerous(c, out _);
        if (defendOnly)
        {
            if (aggressor is null)
                return false;
            if (w.Host.TargetWid == aggressor.Wid)
            {
                StartFight(c, aggressor, "напал");
                return Fight(c);
            }

            Status = $"выбираю цель {aggressor.Name}";
            return c.Submit(new SelectTargetAction(aggressor));
        }

        var current = w.Target is { } t && TargetSelector.IsAllowed(t, target) && !_gaveUp.ContainsKey(t.Wid) && c.InFarmArea(t) ? t : null;
        var mob = aggressor ?? current ?? TargetSelector.Nearest(w, target, _gaveUp.Keys, c.InFarmArea);
        if (mob is null)
        {
            Status = MobNameFilter.ListIsEmpty(target) ? "ищу цель: список мобов пуст — бью только напавших"
                : target.FarmRadius > 0 ? $"ищу цель: в радиусе {target.FarmRadius} м подходящих мобов нет"
                : "ищу цель: подходящих мобов нет";
            return false;
        }

        if (w.Host.TargetWid == mob.Wid)
        {
            StartFight(c, mob, aggressor is not null ? "бьёт нас" : "");
            return Fight(c);
        }

        Status = $"выбираю цель {mob.Name}";
        return c.Submit(new SelectTargetAction(mob));
    }

    private void StartFight(BrainContext c, NpcInfo mob, string why)
    {
        c.Fight.Start(mob);
        _fightStarted = c.Now;
        _targetLostAt = null;
        _lastSword = DateTime.MinValue;
        _lastPetOrder = DateTime.MinValue;
        c.Log.Info($"Бой: {mob.Name} 0x{mob.Wid:X8}, {mob.Offset}{(why.Length > 0 ? " — " + why : "")}");
    }

    // ── Бой ──────────────────────────────────────────────────────────────────

    private bool Fight(BrainContext c)
    {
        var w = c.World;
        var fight = c.Fight;
        var name = fight.MobName;
        var mob = w.Npcs.FirstOrDefault(n => n.Wid == fight.Mob);
        var elapsed = c.Now - _fightStarted;

        if (mob is { IsDead: true })
        {
            c.Log.Info($"{name} убит за {elapsed.TotalSeconds:0} с");
            fight.Killed(mob.Position);
            return AttackedWhileLooting(c);
        }

        if (mob is null)
        {
            c.Log.Info($"{name} пропал из виду");
            return BackToSearch(c);
        }

        // Цель у перса сбрасывается чуть раньше, чем у моба ставится флаг смерти — даём ему время
        if (w.Host.TargetWid != mob.Wid)
        {
            _targetLostAt ??= c.Now;
            if (c.Now - _targetLostAt.Value < TargetLostGrace)
            {
                Status = $"бой: {name} — цель снята, жду";
                return true;
            }

            c.Log.Info($"Цель {name} снята, моб жив — ищу заново");
            return BackToSearch(c);
        }

        _targetLostAt = null;
        if (elapsed.TotalSeconds > c.Settings.Target.MobTimeoutSeconds)
        {
            c.Log.Warning($"{name}: не убит за {c.Settings.Target.MobTimeoutSeconds} с — бросаю на {GiveUpFor.TotalSeconds:0} с");
            _gaveUp[mob.Wid] = c.Now + GiveUpFor;
            c.Submit(new UnselectAction());
            return BackToSearch(c);
        }

        // Другой моб опаснее текущего — бой (и пет) переходят на него. Бьёт меня, а текущий — пета: пет прочнее, пусть держит обоих
        if (MostDangerous(c, out var threat) is { } danger && danger.Wid != mob.Wid && threat > ThreatOf(c, mob))
        {
            c.Log.Info(threat == Threat.HitsMe
                ? $"{danger.Name} бьёт меня — перевожу бой и пета на него, {name} подождёт"
                : $"{danger.Name} бьёт нас — переключаюсь");
            fight.Abort();
            return c.Submit(new SelectTargetAction(danger));
        }

        Status = $"бой: {name}, {mob.Offset}, {elapsed.TotalSeconds:0} с";
        return Attack(c, mob);
    }

    // Лут идёт (его ведёт LootBehavior, он стоит следом), но бой важнее: напали — лут бросаем и сразу ищем цель.
    // Лут выключен — его и не будет, ждать нечего
    private bool AttackedWhileLooting(BrainContext c)
    {
        if (c.Settings.Loot.Enabled && HitsUsFirst(c) && TargetSelector.Aggressor(c.World) is { } aggressor)
        {
            c.Log.Info($"{aggressor.Name} напал во время лута — сначала бой");
            return BackToSearch(c);
        }

        return false;
    }

    // Правила выбора цели: «сначала тех, кто бьёт» (из настроек; при «только защите» — всегда: кроме напавших, бить некого)
    // и «снимать с меня петом» (только с призванным петом)
    private bool HitsUsFirst(BrainContext c) => defendOnly || c.Settings.Target.PreferAggressive;

    private static bool PetTakesAggro(BrainContext c)
        => c.Settings.Target.PetTakesAggro && c.Settings.Pet.Enabled && c.World.Pet is { IsSummoned: true };

    private NpcInfo? MostDangerous(BrainContext c, out Threat threat)
        => TargetSelector.MostDangerous(c.World, HitsUsFirst(c), PetTakesAggro(c), out threat);

    private Threat ThreatOf(BrainContext c, NpcInfo mob)
        => TargetSelector.ThreatOf(mob, c.World, HitsUsFirst(c), PetTakesAggro(c));

    private bool Attack(BrainContext c, NpcInfo mob)
    {
        var w = c.World;
        var combat = c.Settings.Combat;

        // Приказ пету — пока он не бьёт эту цель, не чаще раза в 5 с
        if (c.Settings.Pet.Enabled && w.Pet is { IsSummoned: true } pet && c.Now - _lastPetOrder >= PetOrderPeriod
            && w.Npcs.FirstOrDefault(n => n.Wid == pet.ActiveWid)?.TargetWid != mob.Wid)
        {
            _lastPetOrder = c.Now;
            // Прошлый приказ ещё ждёт подтверждения, но на другого моба (бой переключился) — заменяем, не ждём 6 с
            var order = new PetAttackAction(mob.Wid);
            var stale = c.Mine.OfType<PetAttackAction>().Any(p => p.TargetWid != mob.Wid);
            if (stale ? c.Replace(order) is SubmitStatus.Sent : c.Submit(order))
                return true;
        }

        // Строго по очереди: сначала подходим, потом бьём. Бег, скилл и удар занимают тело — пока одно ждёт или персонаж кастует,
        // другое исполнитель не отправит («занято»), так что здесь только порядок
        if (combat.ComeCloser && c.Runner.Capabilities.Has(Capability.Move) && ComeCloser(c, mob))
            return true;

        if (combat.UseSkill && w.Skill(combat.AttackSkillId) is { IsReady: true }
            && c.Submit(new SkillAction(combat.AttackSkillId, 0, approach: true, $"атака скиллом {combat.AttackSkillId}")))
            return true;

        // Таймер сдвигаем, только когда удар действительно ушёл (а не «занято» или «прошлый ещё ждёт подтверждения»)
        if (combat.UseSword && c.Now - _lastSword >= SwordPeriod && c.Send(new NormalAttackAction()) == SubmitStatus.Sent)
        {
            _lastSword = c.Now;
            return true;
        }

        if (!combat.UseSkill && !combat.UseSword && !(c.Settings.Pet.Enabled && w.Pet is { IsSummoned: true }))
            c.Say("nothing-to-attack", "Нечем бить: включите скилл или меч (или пета)", LogLevel.Warning, 60);

        return true;
    }

    /// <summary>true — подходим (или ещё бежим), бить пока рано.</summary>
    private bool ComeCloser(BrainContext c, NpcInfo mob)
    {
        var w = c.World;
        var distance = c.Settings.Combat.ComeCloserDistance;
        // Только свой подход: фоновый бег в центр фарма — не «уже бежим к мобу», его вытеснит подход или удар
        var running = c.Mine.OfType<MoveAction>().FirstOrDefault();
        // В воздухе — по прямой, с высотой: моб прямо под нами на 40 м — не «рядом» (иначе ждали, пока скилл «как кнопкой»
        // сам спустит перса, 5–9 с). На земле — по горизонтали: моб на склоне или под обрывом не заставляет бегать зря
        var gap = w.Host.Flying == true ? mob.Offset.Direct : mob.Offset.Horizontal;
        if (gap <= distance)
        {
            // Моб уже рядом, а мы ещё бежим к точке — добегаем, не перебивая бег ударом
            if (running is null)
                return false;
            Status = $"бой: {c.Fight.MobName} — добегаю";
            return true;
        }

        var point = PointNear(w.Host.Position, mob.Position, Math.Max(0.5f, distance - ApproachMargin));
        var smart = c.Settings.Combat.ApproachPath == ApproachPath.Smart;
        Status = $"бой: {c.Fight.MobName} — подхожу, {mob.Offset}";
        if (running is null)
        {
            // Тело занято (скилл ещё ждёт, каст) — бежать позже; в лог только когда бег правда начался
            if (c.Send(Approach(w, point, smart)) == SubmitStatus.Sent)
            {
                c.Log.Info($"Подхожу к {mob.Name}: {mob.Offset} > {distance:0} м");
                _lastApproach = c.Now;
            }

            return true;
        }

        // Моб убегает — не добегать до старой точки, а сразу к новой
        if (running.Point.HorizontalDistanceTo(point) > RetargetDistance && c.Now - _lastApproach >= RetargetPeriod)
        {
            c.Log.Info($"{mob.Name} отошёл — бегу к новому месту, {mob.Offset}");
            _lastApproach = c.Now;
            c.Replace(Approach(w, point, smart));
        }

        return true;
    }

    // В воздухе обычный ход ведёт только по горизонтали — летим в точку вместе с высотой моба
    private static MoveAction Approach(WorldState w, Position point, bool smart)
        => w.Host.Flying == true ? new MoveAction(point, ApproachTolerance, fly: true) : new MoveAction(point, ApproachTolerance, smart);

    // Точка на линии «моб → перс» на расстоянии distance от моба
    private static Position PointNear(Position from, Position mob, float distance)
    {
        var total = from.DistanceTo(mob);
        if (total <= distance || total < 0.01f)
            return from;

        var k = distance / total;
        return new Position(mob.X + (from.X - mob.X) * k, mob.Height + (from.Height - mob.Height) * k, mob.Y + (from.Y - mob.Y) * k);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    private bool BackToSearch(BrainContext c)
    {
        c.Fight.Abort();
        return Search(c);
    }

    public void Reset()
    {
        _gaveUp.Clear();
        Status = null;
    }
}

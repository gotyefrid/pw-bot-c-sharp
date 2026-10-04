using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

public enum CombatState
{
    Search,
    Fight,
    Loot,
}

/// <summary>
/// Бой — машина состояний: поиск цели → бой → лут → поиск. Как старый Bot.cs, но по снимкам, без Sleep.
/// <list type="bullet">
/// <item>Поиск: самый опасный моб (<see cref="Threat"/>: бьёт меня → бьёт меня или пета; белый список не важен) →
/// текущая цель, если подходит → ближайший разрешённый.</item>
/// <item>Бой: другой моб опаснее текущего — переходим на него. Приказ пету; «подойти ближе» — сначала подходим, потом бьём;
/// скилл «как кнопкой»; меч раз в ~5 с. Через N с (120) моба бросаем.</item>
/// <item>Лут: до N раз подобрать ближайший разрешённый фильтром предмет в радиусе от места смерти «как мышкой»
/// (клиент сам подводит), пауза 0.7–1.3 с. Кончился — ход отдаём (первым решает копание ресурсов).</item>
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
    private static readonly TimeSpan LootLimit = TimeSpan.FromSeconds(40);
    // Подход: моб ушёл от точки, к которой бежим, дальше этого — бежим к его новому месту (не чаще RetargetPeriod)
    private const float RetargetDistance = 3f;
    // Подход: «дошли» — ближе этого к точке; точку берём ещё на 0.5 м ближе к мобу, чтобы и в худшем случае оказаться
    // ближе нужного (раньше вставали на 0.5 м дальше и делали второй короткий подход)
    private const float ApproachTolerance = 1.5f;
    private const float ApproachMargin = ApproachTolerance + 0.5f;
    private static readonly TimeSpan RetargetPeriod = TimeSpan.FromSeconds(1);
    // Лут — только вокруг места смерти: в старом боте было 20 м от перса, и он бегал к чужому/старому луту
    // Предметы не поднимаются (сумка полна, а мы этого не видим) — сколько неудач подряд терпим и на сколько бросаем
    private const int ItemFailuresToPause = 2;
    private static readonly TimeSpan ItemPause = TimeSpan.FromMinutes(3);

    private readonly Dictionary<uint, DateTime> _gaveUp = [];
    private readonly HashSet<uint> _lootSkipped = [];

    private uint _mob;
    private string _mobName = "";
    private DateTime _fightStarted;
    private DateTime? _targetLostAt;
    private DateTime _lastSword = DateTime.MinValue;
    private DateTime _lastPetOrder = DateTime.MinValue;
    private Position _deathPlace;
    private DateTime _lastApproach;
    private DateTime _lootStarted;
    private int _lootAttempts;
    private DateTime _nextPickup;
    private int _itemFailures;
    private DateTime _onlyMoneyUntil = DateTime.MinValue;

    public string Name => "бой";
    public CombatState State { get; private set; } = CombatState.Search;
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        if (!defendOnly && !c.Settings.Target.KillMobs)
        {
            // Выключили посреди боя или лута — бросаем: иначе бой так и висит «дерусь», а копание ждёт его конца
            if (State != CombatState.Search)
            {
                c.Log.Info($"«Бить мобов» выключено — бросаю {(State == CombatState.Fight ? "бой: " + _mobName : "лут")}");
                State = CombatState.Search;
                _mob = 0;
            }
            Status = "бой выключен";
            return false;
        }

        return State switch
        {
            CombatState.Fight => Fight(c),
            CombatState.Loot => Loot(c),
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
            Status = target.FarmRadius > 0 ? $"ищу цель: в радиусе {target.FarmRadius} м подходящих мобов нет" : "ищу цель: подходящих мобов нет";
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
        State = CombatState.Fight;
        _mob = mob.Wid;
        _mobName = mob.Name;
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
        var mob = w.Npcs.FirstOrDefault(n => n.Wid == _mob);
        var elapsed = c.Now - _fightStarted;

        if (mob is not null)
            _deathPlace = mob.Position;

        if (mob is { IsDead: true })
        {
            c.Log.Info($"{_mobName} убит за {elapsed.TotalSeconds:0} с");
            StartLoot(c);
            return Loot(c);
        }

        if (mob is null)
        {
            c.Log.Info($"{_mobName} пропал из виду");
            return BackToSearch(c);
        }

        // Цель у перса сбрасывается чуть раньше, чем у моба ставится флаг смерти — даём ему время
        if (w.Host.TargetWid != _mob)
        {
            _targetLostAt ??= c.Now;
            if (c.Now - _targetLostAt.Value < TargetLostGrace)
            {
                Status = $"бой: {_mobName} — цель снята, жду";
                return true;
            }

            c.Log.Info($"Цель {_mobName} снята, моб жив — ищу заново");
            return BackToSearch(c);
        }

        _targetLostAt = null;
        if (elapsed.TotalSeconds > c.Settings.Target.MobTimeoutSeconds)
        {
            c.Log.Warning($"{_mobName}: не убит за {c.Settings.Target.MobTimeoutSeconds} с — бросаю на {GiveUpFor.TotalSeconds:0} с");
            _gaveUp[_mob] = c.Now + GiveUpFor;
            c.Submit(new UnselectAction());
            return BackToSearch(c);
        }

        // Другой моб опаснее текущего — бой (и пет) переходят на него. Бьёт меня, а текущий — пета: пет прочнее, пусть держит обоих
        if (MostDangerous(c, out var threat) is { } danger && danger.Wid != _mob && threat > ThreatOf(c, mob))
        {
            c.Log.Info(threat == Threat.HitsMe
                ? $"{danger.Name} бьёт меня — перевожу бой и пета на него, {_mobName} подождёт"
                : $"{danger.Name} бьёт нас — переключаюсь");
            State = CombatState.Search;
            return c.Submit(new SelectTargetAction(danger));
        }

        Status = $"бой: {_mobName}, {mob.Offset}, {elapsed.TotalSeconds:0} с";
        return Attack(c, mob);
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
            Status = $"бой: {_mobName} — добегаю";
            return true;
        }

        var point = PointNear(w.Host.Position, mob.Position, Math.Max(0.5f, distance - ApproachMargin));
        var smart = c.Settings.Combat.ApproachPath == ApproachPath.Smart;
        Status = $"бой: {_mobName} — подхожу, {mob.Offset}";
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

    // ── Лут ──────────────────────────────────────────────────────────────────

    private void StartLoot(BrainContext c)
    {
        State = CombatState.Loot;
        _lootStarted = c.Now;
        _lootAttempts = 0;
        _lootSkipped.Clear();
        _nextPickup = DateTime.MinValue;
    }

    private bool Loot(BrainContext c)
    {
        var w = c.World;
        var loot = c.Settings.Loot;
        if (!loot.Enabled)
            return LootDone();

        // Напали — лут подождёт
        if (HitsUsFirst(c) && TargetSelector.Aggressor(w) is { } aggressor)
        {
            c.Log.Info($"{aggressor.Name} напал во время лута — сначала бой");
            return BackToSearch(c);
        }

        if (c.Now - _lootStarted > LootLimit)
        {
            c.Log.Warning("Лут занял слишком долго — дальше");
            return LootDone();
        }

        // Подбор занимает тело: ждём прошлый подбор, бег, каст (скилл, заказанный ещё по живому, кастуется после смерти;
        // клиент 1.4.6 при касте молча отбрасывает подбор). Ждём здесь, а не отправляем «в занято», — иначе считали бы попытки
        if (c.Runner.BodyBusy(w) is { } busy)
        {
            Status = $"лут: жду — {busy}";
            return true;
        }

        if (_lootAttempts >= loot.Attempts)
        {
            c.Log.Info($"Лут: {_lootAttempts} из {loot.Attempts}");
            return LootDone();
        }

        if (c.Now < _nextPickup)
        {
            Status = "лут: пауза";
            return true;
        }

        var onlyMoney = c.Now < _onlyMoneyUntil;
        if (w.BagFull)
            c.Say("bag-full", "Сумка полна — подбираю только монеты и то, что ляжет в начатые стопки", LogLevel.Warning, 300);

        // Лут ищем вокруг места смерти, а к месту смерти не ходим: подбор «как мышкой» сам подводит к предмету
        var item = w.GroundItems
            .Where(i => i.Position.HorizontalDistanceTo(_deathPlace) <= loot.Radius && !_lootSkipped.Contains(i.Id) && LootFilter.Allows(loot, i))
            .Where(i => w.FitsInBag(i) && (!onlyMoney || i.Kind == GroundItemKind.Money))
            .OrderBy(i => i.Offset.Direct)
            .FirstOrDefault();
        if (item is null)
        {
            if (_lootAttempts > 0)
                c.Log.Info($"Лут: подобрано {_lootAttempts}, больше нечего");
            return LootDone();
        }

        _lootAttempts++;
        Status = $"лут: {item.Name} ({_lootAttempts}/{loot.Attempts})";
        return c.Submit(new PickupAction(item, approach: true));
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is not PickupAction pickup)
            return;

        if (outcome.Status != ActionStatus.Confirmed)
            _lootSkipped.Add(pickup.Item.Id);

        // Монеты идут в кошелёк — по ним о сумке не судим
        if (pickup.Item.Kind != GroundItemKind.Money && outcome.Status != ActionStatus.Failed)
        {
            _itemFailures = outcome.Status == ActionStatus.Confirmed ? 0 : _itemFailures + 1;
            if (_itemFailures >= ItemFailuresToPause)
            {
                _itemFailures = 0;
                _onlyMoneyUntil = c.Now + ItemPause;
                c.Log.Warning($"Предметы {ItemFailuresToPause} раза подряд не поднялись (сумка полна?) — {ItemPause.TotalMinutes:0} мин подбираю только монеты");
            }
        }
        _nextPickup = c.Now + TimeSpan.FromMilliseconds(c.Random.Next(700, 1300));
    }

    // Лут кончился: цель не выбираем сразу — ход отдаём, на следующем шаге первым решает копание ресурсов
    private bool LootDone()
    {
        State = CombatState.Search;
        _mob = 0;
        return false;
    }

    private bool BackToSearch(BrainContext c)
    {
        State = CombatState.Search;
        _mob = 0;
        return Search(c);
    }

    public void Reset()
    {
        State = CombatState.Search;
        _mob = 0;
        _gaveUp.Clear();
        _lootSkipped.Clear();
        _itemFailures = 0;
        _onlyMoneyUntil = DateTime.MinValue;
        Status = null;
    }
}

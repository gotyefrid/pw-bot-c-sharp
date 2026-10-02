using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
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
/// <item>Поиск: моб, бьющий перса/пета (белый список не важен) → текущая цель, если подходит → ближайший разрешённый.</item>
/// <item>Бой: приказ пету; скилл «как кнопкой» (сам подходит); меч раз в ~5 с; «подойти ближе», если меч выключен;
/// во время каста ничего нового. Через N с (120) моба бросаем.</item>
/// <item>Лут: дойти до места смерти (дальше 3 м), до N раз подобрать ближайший разрешённый фильтром предмет
/// не дальше 10 м от места смерти «как мышкой», ждать до 10 с, пауза 0.7–1.3 с.</item>
/// </list>
/// </summary>
public sealed class CombatBehavior : IBehavior
{
    private static readonly TimeSpan SwordPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PetOrderPeriod = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TargetLostGrace = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan GiveUpFor = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LootLimit = TimeSpan.FromSeconds(40);
    private const float KillPlaceNear = 3f;
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
    private DateTime _lootStarted;
    private bool _walkedToDeathPlace;
    private int _lootAttempts;
    private DateTime _nextPickup;
    private int _itemFailures;
    private DateTime _onlyMoneyUntil = DateTime.MinValue;

    public string Name => "бой";
    public CombatState State { get; private set; } = CombatState.Search;
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        if (!c.Settings.Target.KillMobs)
        {
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

        var aggressor = target.PreferAggressive ? TargetSelector.Aggressor(w) : null;
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
        c.Log.Info($"Бой: {mob.Name} 0x{mob.Wid:X8}, {mob.Distance:0.0} м{(why.Length > 0 ? " — " + why : "")}");
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

        // Нас бьёт другой моб, а текущий — нет: сначала тот, кто бьёт
        var aggressor = c.Settings.Target.PreferAggressive ? TargetSelector.Aggressor(w) : null;
        var petWid = w.Pet?.ActiveWid ?? 0;
        if (aggressor is not null && aggressor.Wid != _mob && mob.TargetWid != w.Host.Wid && (petWid == 0 || mob.TargetWid != petWid))
        {
            c.Log.Info($"{aggressor.Name} бьёт нас — переключаюсь");
            State = CombatState.Search;
            return c.Submit(new SelectTargetAction(aggressor));
        }

        Status = $"бой: {_mobName}, {mob.Distance:0.0} м, {elapsed.TotalSeconds:0} с";
        return Attack(c, mob);
    }

    private bool Attack(BrainContext c, NpcInfo mob)
    {
        var w = c.World;
        var combat = c.Settings.Combat;

        // Приказ пету — пока он не бьёт эту цель, не чаще раза в 5 с
        if (c.Settings.Pet.Enabled && w.Pet is { IsSummoned: true } pet && c.Now - _lastPetOrder >= PetOrderPeriod
            && w.Npcs.FirstOrDefault(n => n.Wid == pet.ActiveWid)?.TargetWid != mob.Wid)
        {
            _lastPetOrder = c.Now;
            if (c.Submit(new PetAttackAction(mob.Wid)))
                return true;
        }

        // Во время каста новое не отправляем — сбили бы каст
        if (w.Host.IsCasting)
            return true;

        if (combat.UseSkill && w.Skill(combat.AttackSkillId) is { IsReady: true }
            && !c.Runner.IsPending($"скилл {combat.AttackSkillId}"))
            return c.Submit(new SkillAction(combat.AttackSkillId, 0, approach: true, $"атака скиллом {combat.AttackSkillId}"));

        // Таймер сдвигаем, только когда удар действительно ушёл (а не «прошлый ещё ждёт подтверждения»)
        var sword = new NormalAttackAction();
        if (combat.UseSword && c.Now - _lastSword >= SwordPeriod && !c.Runner.IsPending(sword.Key))
        {
            _lastSword = c.Now;
            return c.Submit(sword);
        }

        if (!combat.UseSword && combat.ComeCloser && mob.Distance > combat.ComeCloserDistance && c.Runner.Actions.CanMove
            && !c.Runner.IsPending("движение"))
        {
            var point = PointNear(w.Host.Position, mob.Position, combat.ComeCloserDistance - 1);
            c.Log.Info($"Подхожу к {mob.Name}: {mob.Distance:0.0} м > {combat.ComeCloserDistance:0} м");
            return c.Submit(new MoveAction(point, tolerance: 1.5f));
        }

        if (!combat.UseSkill && !combat.UseSword && !(c.Settings.Pet.Enabled && w.Pet is { IsSummoned: true }))
            c.Say("nothing-to-attack", "Нечем бить: включите скилл или меч (или пета)", LogLevel.Warning, 60);

        return true;
    }

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
        _walkedToDeathPlace = false;
        _lootAttempts = 0;
        _lootSkipped.Clear();
        _nextPickup = DateTime.MinValue;
    }

    private bool Loot(BrainContext c)
    {
        var w = c.World;
        var loot = c.Settings.Loot;
        if (!loot.Enabled)
            return BackToSearch(c);

        // Напали — лут подождёт
        if (c.Settings.Target.PreferAggressive && TargetSelector.Aggressor(w) is { } aggressor)
        {
            c.Log.Info($"{aggressor.Name} напал во время лута — сначала бой");
            return BackToSearch(c);
        }

        if (c.Now - _lootStarted > LootLimit)
        {
            c.Log.Warning("Лут занял слишком долго — дальше");
            return BackToSearch(c);
        }

        if (c.Runner.IsPending("движение") || c.Runner.Pending.Any(a => a is PickupAction))
        {
            Status = "лут: жду";
            return true;
        }

        // Пока персонаж кастует, клиент молча отбрасывает подбор (на Comeback 1.4.6 видно по коду: работа «каст» блокирует
        // PickupObject). Каст после смерти моба бывает: скилл, заказанный ещё по живому
        if (w.Host.IsCasting)
        {
            Status = "лут: жду конца каста";
            return true;
        }

        // Лут падает вокруг места смерти — если моба убил пет вдалеке, сначала идём туда.
        // Не умеем ходить — подбор «как мышкой» сам подведёт к предмету
        if (!_walkedToDeathPlace && c.Runner.Actions.CanMove)
        {
            _walkedToDeathPlace = true;
            var distance = w.Host.Position.DistanceTo(_deathPlace);
            if (distance > KillPlaceNear)
            {
                Status = "лут: иду к месту смерти";
                c.Log.Info($"Иду к месту смерти, {distance:0.0} м");
                return c.Submit(new MoveAction(_deathPlace, KillPlaceNear));
            }
        }

        if (_lootAttempts >= loot.Attempts)
        {
            c.Log.Info($"Лут: {_lootAttempts} из {loot.Attempts}");
            return BackToSearch(c);
        }

        if (c.Now < _nextPickup)
        {
            Status = "лут: пауза";
            return true;
        }

        var onlyMoney = c.Now < _onlyMoneyUntil;
        if (w.BagFull)
            c.Say("bag-full", "Сумка полна — подбираю только монеты и то, что ляжет в начатые стопки", LogLevel.Warning, 300);

        var item = w.GroundItems
            .Where(i => i.Position.HorizontalDistanceTo(_deathPlace) <= loot.Radius && !_lootSkipped.Contains(i.Id) && LootFilter.Allows(loot, i))
            .Where(i => w.FitsInBag(i) && (!onlyMoney || i.Kind == GroundItemKind.Money))
            .OrderBy(i => i.Distance)
            .FirstOrDefault();
        if (item is null)
        {
            if (_lootAttempts > 0)
                c.Log.Info($"Лут: подобрано {_lootAttempts}, больше нечего");
            return BackToSearch(c);
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

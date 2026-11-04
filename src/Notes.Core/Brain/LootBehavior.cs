using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Лут после убийства (<see cref="FightPhase.Looting"/>): до N раз подобрать ближайший разрешённый фильтром предмет в радиусе
/// от места смерти «как мышкой» (клиент сам подводит), пауза 0.7–1.3 с. Кончился — ход отдаём (первым решает копание ресурсов).
/// Стоит сразу за боем: моб умер — лут начинается на том же снимке; напали во время лута — бой бросает лут сам.
/// Лут, до которого не дошло (сразу напал следующий моб), не теряется: в фоне, на каждом снимке и не занимая ход
/// (<see cref="Observe"/>), запоминаем всё, что подходит фильтру и упало у места смерти в первые <see cref="DropWatch"/>
/// после убийства. Подбираем запомненное на следующем луте вместе с новым, ближайшее первым; на него — лишние попытки и время.
/// Пропало с земли, не поднялось или лежит дольше <see cref="RememberFor"/> — забываем.
/// </summary>
public sealed class LootBehavior : IBehavior, IWatchingBehavior
{
    /// <summary>Столько после убийства смотрим, что упало у места смерти (лут ложится за 0–0,1 с, снимки — раз в 0,25 с).</summary>
    public static readonly TimeSpan DropWatch = TimeSpan.FromSeconds(3);

    /// <summary>Запомненный лут дольше этого не ждёт.</summary>
    public static readonly TimeSpan RememberFor = TimeSpan.FromMinutes(10);

    // На каждый запомненный предмет — сверх обычного срока лута, но не дольше MaxLootLimit
    private static readonly TimeSpan PerRemembered = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxLootLimit = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan LootLimit = TimeSpan.FromSeconds(40);
    // Лут ложится на землю чуть позже, чем моб помечен мёртвым (замер: 0–0,1 с), а смерть бой видит на том же снимке, что и
    // лут: пусто на первом снимке — ещё не «ничего не упало». Ждём ещё снимок-другой, потом уже идём дальше
    private static readonly TimeSpan DropWait = TimeSpan.FromSeconds(0.5);
    // Предметы не поднимаются (сумка полна, а мы этого не видим) — сколько неудач подряд терпим и на сколько бросаем
    private const int ItemFailuresToPause = 2;
    private static readonly TimeSpan ItemPause = TimeSpan.FromMinutes(3);

    private readonly HashSet<uint> _skipped = [];
    // Недавние убийства: где и когда (за ними смотрим DropWatch); запомненный лут: id → когда увидели
    private readonly List<(Position At, DateTime Time)> _recentKills = [];
    private readonly Dictionary<uint, DateTime> _remembered = [];
    private int _seenKill;
    // На этот лут: что запомнено с прошлых боёв (лишние попытки и время) и сколько из него подобрали
    private readonly HashSet<uint> _past = [];
    private int _fromPast;

    private int _kill;
    private DateTime _started;
    private int _attempts;
    private DateTime _nextPickup;
    private int _itemFailures;
    private DateTime _onlyMoneyUntil = DateTime.MinValue;

    public string Name => "лут";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        var fight = c.Fight;
        if (fight.Phase != FightPhase.Looting)
            return false;

        // Новое убийство — лут с нуля (лут ищем вокруг места смерти, а к месту смерти не ходим: подбор сам подводит)
        if (_kill != fight.Kill)
        {
            _kill = fight.Kill;
            _started = c.Now;
            _attempts = 0;
            _skipped.Clear();
            _nextPickup = DateTime.MinValue;
            // Лут этого убийства ещё не запомнен (падает после смерти): всё запомненное — с прошлых боёв
            _past.Clear();
            _past.UnionWith(_remembered.Keys);
            _fromPast = 0;
        }

        var w = c.World;
        var loot = c.Settings.Loot;
        if (!loot.Enabled)
            return Done(c);

        var limit = LootLimit + TimeSpan.FromTicks(PerRemembered.Ticks * _past.Count);
        if (c.Now - _started > (limit < MaxLootLimit ? limit : MaxLootLimit))
        {
            c.Log.Warning("Лут занял слишком долго — дальше");
            return Done(c);
        }

        // Подбор занимает тело: ждём прошлый подбор, бег, каст (скилл, заказанный ещё по живому, кастуется после смерти;
        // клиент 1.4.6 при касте молча отбрасывает подбор). Ждём здесь, а не отправляем «в занято», — иначе считали бы попытки
        if (c.Runner.BodyBusy(w) is { } busy)
        {
            Status = $"лут: жду — {busy}";
            return true;
        }

        if (_attempts >= loot.Attempts + _past.Count)
        {
            c.Log.Info($"Лут: {_attempts} из {loot.Attempts + _past.Count}{Past()}");
            return Done(c);
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
            .Where(i => (i.Position.HorizontalDistanceTo(fight.KilledAt) <= loot.Radius || _remembered.ContainsKey(i.Id))
                        && !_skipped.Contains(i.Id) && LootFilter.Allows(loot, i))
            .Where(i => w.FitsInBag(i) && (!onlyMoney || i.Kind == GroundItemKind.Money))
            .OrderBy(i => i.Offset.Direct)
            .FirstOrDefault();
        if (item is null)
        {
            if (_attempts == 0 && c.Now - _started < DropWait)
            {
                Status = "лут: жду, пока упадёт";
                return true;
            }

            if (_attempts > 0)
                c.Log.Info($"Лут: подобрано {_attempts}, больше нечего{Past()}");
            return Done(c);
        }

        _attempts++;
        Status = $"лут: {item.Name} ({_attempts}/{loot.Attempts + _past.Count})";
        return c.Submit(new PickupAction(item));
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is not PickupAction pickup)
            return;

        // Подбор отменило более важное (лечение пета) — не неудача: попытку возвращаем, предмет поднимем следом
        if (outcome.Status == ActionStatus.Cancelled)
        {
            _attempts = Math.Max(0, _attempts - 1);
            return;
        }

        if (outcome.Status != ActionStatus.Confirmed)
            _skipped.Add(pickup.Item.Id);
        else if (_past.Contains(pickup.Item.Id))
            _fromPast++;
        // Поднят или не поднялся — больше не ждём
        _remembered.Remove(pickup.Item.Id);

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

    /// <summary>
    /// В фоне, на каждом снимке: что упало у мест недавних убийств и подходит фильтру — запомнить; пропавшее с земли и старое —
    /// забыть. Ход не занимает: бой идёт дальше.
    /// </summary>
    public void Observe(BrainContext c, IBehavior? acted)
    {
        var loot = c.Settings.Loot;
        var fight = c.Fight;
        if (fight.Kill != _seenKill)
        {
            _seenKill = fight.Kill;
            _recentKills.Add((fight.KilledAt, c.Now));
        }
        _recentKills.RemoveAll(k => c.Now - k.Time > DropWatch);
        if (!loot.Enabled)
        {
            _remembered.Clear();
            return;
        }

        var ground = c.World.GroundItems;
        foreach (var item in ground)
        {
            if (!_remembered.ContainsKey(item.Id) && item.Kind != GroundItemKind.Resource && LootFilter.Allows(loot, item)
                && _recentKills.Any(k => item.Position.HorizontalDistanceTo(k.At) <= loot.Radius))
                _remembered[item.Id] = c.Now;
        }
        foreach (var id in _remembered.Keys.ToList())
        {
            if (c.Now - _remembered[id] > RememberFor || !ground.Any(i => i.Id == id))
                _remembered.Remove(id);
        }
    }

    private string Past() => _fromPast > 0 ? $" (из них {_fromPast} с прошлых боёв)" : "";

    // Лут кончился: цель не выбираем сразу — ход отдаём, на следующем шаге первым решает копание ресурсов
    private static bool Done(BrainContext c)
    {
        c.Fight.Abort();
        return false;
    }

    public void Reset()
    {
        _skipped.Clear();
        _recentKills.Clear();
        _remembered.Clear();
        _past.Clear();
        _itemFailures = 0;
        _onlyMoneyUntil = DateTime.MinValue;
        Status = null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Лут после убийства (<see cref="FightPhase.Looting"/>): до N раз подобрать ближайший разрешённый фильтром предмет в радиусе
/// от места смерти «как мышкой» (клиент сам подводит), пауза 0.7–1.3 с. Кончился — ход отдаём (первым решает копание ресурсов).
/// Стоит сразу за боем: моб умер — лут начинается на том же снимке; напали во время лута — бой бросает лут сам.
/// </summary>
public sealed class LootBehavior : IBehavior
{
    private static readonly TimeSpan LootLimit = TimeSpan.FromSeconds(40);
    // Предметы не поднимаются (сумка полна, а мы этого не видим) — сколько неудач подряд терпим и на сколько бросаем
    private const int ItemFailuresToPause = 2;
    private static readonly TimeSpan ItemPause = TimeSpan.FromMinutes(3);

    private readonly HashSet<uint> _skipped = [];

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
        }

        var w = c.World;
        var loot = c.Settings.Loot;
        if (!loot.Enabled)
            return Done(c);

        if (c.Now - _started > LootLimit)
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

        if (_attempts >= loot.Attempts)
        {
            c.Log.Info($"Лут: {_attempts} из {loot.Attempts}");
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
            .Where(i => i.Position.HorizontalDistanceTo(fight.KilledAt) <= loot.Radius && !_skipped.Contains(i.Id) && LootFilter.Allows(loot, i))
            .Where(i => w.FitsInBag(i) && (!onlyMoney || i.Kind == GroundItemKind.Money))
            .OrderBy(i => i.Offset.Direct)
            .FirstOrDefault();
        if (item is null)
        {
            if (_attempts > 0)
                c.Log.Info($"Лут: подобрано {_attempts}, больше нечего");
            return Done(c);
        }

        _attempts++;
        Status = $"лут: {item.Name} ({_attempts}/{loot.Attempts})";
        return c.Submit(new PickupAction(item, approach: true));
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is not PickupAction pickup)
            return;

        if (outcome.Status != ActionStatus.Confirmed)
            _skipped.Add(pickup.Item.Id);

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
    private static bool Done(BrainContext c)
    {
        c.Fight.Abort();
        return false;
    }

    public void Reset()
    {
        _skipped.Clear();
        _itemFailures = 0;
        _onlyMoneyUntil = DateTime.MinValue;
        Status = null;
    }
}

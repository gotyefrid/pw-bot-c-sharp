using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.World;

namespace BotCH.Core.Actions;

/// <summary>
/// Чем и когда кормить пета. Перенос исправленной логики старого бота (Action.FeedPet, 69df3b4):
/// <list type="bullet">
/// <item>отказ ≠ «не ест»: сытость обновляется не сразу, а перезарядку корма прочитать нельзя (перс+0xBF4 всегда 0),
/// поэтому повтор через пару секунд получает отказ сервера;</item>
/// <item>корм, который пет хоть раз съел, никогда не считается несъедобным;</item>
/// <item>«не ест» — только после 2 отказов подряд у корма, который ни разу не был съеден;</item>
/// <item>пауза 30 с после удачного кормления и 10 с после отказа;</item>
/// <item>сменился пет — всё забыть. Ключ пета — клетка: WID меняется при каждом призыве.</item>
/// </list>
/// </summary>
public sealed class PetFeeding(TimeSpan? pauseAfterSuccess = null, TimeSpan? pauseAfterRefusal = null, int refusalsToGiveUp = 2)
{
    private readonly TimeSpan _pauseAfterSuccess = pauseAfterSuccess ?? TimeSpan.FromSeconds(30);
    private readonly TimeSpan _pauseAfterRefusal = pauseAfterRefusal ?? TimeSpan.FromSeconds(10);
    private readonly HashSet<uint> _eaten = [];
    private readonly HashSet<uint> _notEaten = [];
    private readonly Dictionary<uint, int> _refusals = [];
    private DateTime _nextAttempt = DateTime.MinValue;
    private int? _cage;

    /// <summary>Корм, который этот пет не ест (для лога/окна).</summary>
    public IReadOnlyCollection<uint> NotEaten => _notEaten;

    /// <summary>
    /// Корм, которым кормить сейчас, или null (причина в <paramref name="why"/>; пустая — кормить не нужно).
    /// Самый малый по верности из тех, что пет ест или ещё не пробовал.
    /// </summary>
    public InventoryItem? Choose(WorldState world, out string why)
    {
        why = "";
        var pet = world.Pet;
        if (pet is not { IsSummoned: true } || pet.InCage(pet.ActiveCage!.Value) is not { IsHungry: true })
            return null;

        Remember(pet.ActiveCage.Value);
        if (world.Time < _nextAttempt)
        {
            why = $"пауза после кормления ещё {(_nextAttempt - world.Time).TotalSeconds:0} с";
            return null;
        }

        var food = world.Inventory
            .Where(i => i.IsPetFood && i.Count > 0 && !_notEaten.Contains(i.Tid))
            .OrderBy(i => i.FoodLoyalty)
            .FirstOrDefault();
        if (food is null)
            why = _notEaten.Count > 0 ? "нет корма, который ест пет" : "нет корма в сумке";
        return food;
    }

    /// <summary>Итог кормления (по <see cref="UseItemAction"/>): съел — стопка уменьшилась, иначе отказ.</summary>
    public string Report(uint tid, bool eaten, DateTime time)
    {
        if (eaten)
        {
            _eaten.Add(tid);
            _refusals.Remove(tid);
            _nextAttempt = time + _pauseAfterSuccess;
            return $"пет съел корм {tid}";
        }

        _nextAttempt = time + _pauseAfterRefusal;
        if (_eaten.Contains(tid))
            return $"корм {tid} сейчас не принят (перезарядка?), позже ещё раз";

        _refusals.TryGetValue(tid, out var refusals);
        _refusals[tid] = ++refusals;
        if (refusals < refusalsToGiveUp)
            return $"корм {tid} не принят, позже ещё раз";

        _notEaten.Add(tid);
        return $"пет не ест корм {tid} — берём другой";
    }

    // У другого пета свой вкус — всё забываем
    private void Remember(int cage)
    {
        var changed = _cage is not null && _cage != cage;
        _cage = cage;
        if (!changed)
            return;

        _eaten.Clear();
        _notEaten.Clear();
        _refusals.Clear();
        _nextAttempt = DateTime.MinValue;
    }
}

public enum PotionKind
{
    Hp,
    Mp,
}

/// <summary>
/// Какую банку пить: самую слабую подходящую по уровню (как старый бот). После выпитой банки такую же (HP или MP)
/// не пить, пока она действует (время из описания банки, у малых 10 с — подтверждено в игре).
/// </summary>
public sealed class PotionPolicy
{
    private readonly Dictionary<PotionKind, DateTime> _activeUntil = [];

    public InventoryItem? Choose(WorldState world, PotionKind kind, out string why)
    {
        why = "";
        if (_activeUntil.TryGetValue(kind, out var until) && world.Time < until)
        {
            why = $"банка {kind} ещё действует {(until - world.Time).TotalSeconds:0} с";
            return null;
        }

        if (kind == PotionKind.Hp && !world.Host.HpPotionReady)
        {
            why = "банка HP на перезарядке";
            return null;
        }

        var potion = world.Inventory
            .Where(i => i.Potion is { } p && p.RequiredLevel <= world.Host.Level && Amount(p, kind) > 0 && i.Count > 0)
            .OrderBy(i => Amount(i.Potion!, kind))
            .FirstOrDefault();
        if (potion is null)
            why = $"нет банок {kind}";
        return potion;
    }

    /// <summary>Банку выпили (стопка уменьшилась) — такую же не пить, пока действует.</summary>
    public void Drunk(InventoryItem potion, PotionKind kind, DateTime time)
    {
        var seconds = kind == PotionKind.Hp ? potion.Potion!.HpSeconds : potion.Potion!.MpSeconds;
        _activeUntil[kind] = time + TimeSpan.FromSeconds(Math.Max(seconds, 1));
    }

    private static int Amount(PotionInfo potion, PotionKind kind) => kind == PotionKind.Hp ? potion.Hp : potion.Mp;
}

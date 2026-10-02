using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Копать ресурсы в радиусе фарма. Стоит перед боем: пока в радиусе есть ресурсы из списка — копаем их подряд, потом мобы.
/// Бой и лут не перебиваем; напали на перса или пета — бросаем копание, бой убивает нападающего, потом копаем дальше.
/// Без инструмента (кирки) в сумке к ресурсам не подходим.
/// </summary>
public sealed class GatherBehavior(CombatBehavior combat, IReadOnlyCollection<uint> tools) : IBehavior
{
    // Не вышло (нет инструмента, не дошли, сбили N раз подряд) — ресурс бросаем на время
    private static readonly TimeSpan SkipFor = TimeSpan.FromMinutes(3);
    private const int KnockdownsToSkip = 3;

    private readonly Dictionary<uint, DateTime> _skipped = [];
    private readonly Dictionary<uint, int> _knockdowns = [];

    public string Name => "сбор";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var loot = c.Settings.Loot;
        if (!loot.Enabled || !loot.PickResources)
            return false;

        var w = c.World;
        var pending = c.Runner.Pending.OfType<GatherAction>().FirstOrDefault();

        // Напали — копание бросаем, дальше решает бой
        if (TargetSelector.Aggressor(w) is { } aggressor)
        {
            if (pending is not null)
            {
                c.Log.Info($"{aggressor.Name} напал — бросаю копать {pending.Item.Name}");
                c.Runner.Cancel(pending.Key, "напали — сначала бой", w);
            }

            return false;
        }

        if (pending is not null)
        {
            Status = $"копаю {pending.Item.Name}";
            return true;
        }

        // Идёт бой или лут — не перебиваем
        if (combat.State != CombatState.Search)
            return false;

        if (tools.Count == 0)
        {
            c.Say("gather-no-tools", "Копать не буду: для этого сервера неизвестно, какой предмет — кирка", LogLevel.Warning, 600);
            return false;
        }

        if (!w.Inventory.Any(i => tools.Contains(i.Tid)))
        {
            c.Say("gather-no-pickaxe", "Копать не буду: нет кирки в сумке", LogLevel.Warning, 300);
            return false;
        }

        if (w.BagFull)
        {
            c.Say("gather-bag-full", "Копать не буду: сумка полна", LogLevel.Warning, 300);
            return false;
        }

        foreach (var expired in _skipped.Where(s => s.Value <= c.Now).Select(s => s.Key).ToList())
            _skipped.Remove(expired);

        var resource = w.GroundItems
            .Where(i => i.Kind == GroundItemKind.Resource && !_skipped.ContainsKey(i.Id) && c.InFarmArea(i.Position))
            .Where(i => Settings.LootFilter.AllowsGather(loot, i.Name))
            .OrderBy(i => i.Distance)
            .FirstOrDefault();
        if (resource is null)
            return false;

        Status = $"копаю {resource.Name}";
        var sent = c.Send(new GatherAction(resource));
        if (sent == SubmitStatus.Sent)
            c.Log.Info($"Копаю {resource.Name}, {resource.Distance:0.0} м");
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is not GatherAction gather)
            return;

        var id = gather.Item.Id;
        switch (outcome.Status)
        {
            case ActionStatus.Confirmed:
                _knockdowns.Remove(id);
                break;
            case ActionStatus.Cancelled:
                break;
            case ActionStatus.Rejected when gather.KnockedDown:
                // Сбили — копаем снова (нападающего сначала убьёт бой); сбивают раз за разом — бросаем
                _knockdowns[id] = _knockdowns.TryGetValue(id, out var n) ? n + 1 : 1;
                if (_knockdowns[id] >= KnockdownsToSkip)
                    Skip(c, gather, $"сбили {KnockdownsToSkip} раза подряд");
                break;
            default:
                Skip(c, gather, outcome.Details);
                break;
        }
    }

    private void Skip(BrainContext c, GatherAction gather, string why)
    {
        _knockdowns.Remove(gather.Item.Id);
        _skipped[gather.Item.Id] = c.Now + SkipFor;
        c.Log.Warning($"{gather.Item.Name}: {why} — не копаю {SkipFor.TotalMinutes:0} мин");
    }

    public void Reset()
    {
        _skipped.Clear();
        _knockdowns.Clear();
        Status = null;
    }
}

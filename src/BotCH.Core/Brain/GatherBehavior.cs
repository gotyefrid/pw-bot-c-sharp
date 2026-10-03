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
/// Без инструмента (кирки) в сумке к ресурсам не подходим. Сумка полна — копаем только то, чья добыча (по прошлым копкам)
/// ляжет в начатые стопки: <paramref name="fitsInStacks"/> (название ресурса, мир) — знает, что даёт ресурс.
/// </summary>
public sealed class GatherBehavior(CombatBehavior combat, IReadOnlyCollection<uint> tools, Func<string, WorldState, bool>? fitsInStacks = null)
    : IBehavior
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

        // Напали — ход бою; копание фоновое, его прервёт первое же действие боя (решает исполнитель)
        if (TargetSelector.Aggressor(w) is { } aggressor)
        {
            if (pending is not null)
                c.Say($"gather-attacked-{pending.Item.Id:X8}", $"{aggressor.Name} напал — бросаю копать {pending.Item.Name}", seconds: 600);
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

        foreach (var expired in _skipped.Where(s => s.Value <= c.Now).Select(s => s.Key).ToList())
            _skipped.Remove(expired);

        var resource = w.GroundItems
            .Where(i => i.Kind == GroundItemKind.Resource && !_skipped.ContainsKey(i.Id) && c.InFarmArea(i.Position))
            .Where(i => Settings.LootFilter.AllowsGather(loot, i.Name))
            .Where(i => !w.BagFull || fitsInStacks?.Invoke(i.Name, w) == true)
            .OrderBy(i => i.Distance)
            .FirstOrDefault();
        if (resource is null)
        {
            if (w.BagFull)
                c.Say("gather-bag-full", "Копать не буду: сумка полна, а добыча ресурсов рядом не ляжет в начатые стопки", LogLevel.Warning, 300);
            return false;
        }

        if (w.BagFull)
            c.Say($"gather-into-stack-{resource.Id:X8}", $"Сумка полна, но добыча {resource.Name} ляжет в начатую стопку — копаю", seconds: 600);

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

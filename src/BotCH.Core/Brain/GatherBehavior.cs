using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Копать ресурсы: что и где — <see cref="GatherScope"/> (фарм — в радиусе фарма по списку лута, обход — у точки по своему
/// списку). Стоит перед боем: пока рядом есть ресурсы из списка — копаем их подряд, потом мобы.
/// Бой и лут не перебиваем; напали на перса или пета — бросаем копание, бой убивает нападающего, потом копаем дальше.
/// Без инструмента (кирки) в сумке к ресурсам не подходим. Сумка полна — копаем только то, чья добыча (по справочнику игры,
/// <see cref="GroundItem.Mine"/>) целиком ляжет в начатые стопки. Ресурс выше уровня персонажа (по справочнику) не копаем. «Нересурсы» (<see cref="GroundItem.Special"/>: квестовые,
/// особые) — только если их название явно в списке, и тогда без всяких условий: просто пробуем копать.
/// </summary>
public sealed class GatherBehavior(CombatBehavior combat, IReadOnlyCollection<uint> tools, GatherScope? scope = null) : IBehavior
{
    private readonly GatherScope _scope = scope ?? GatherScope.FarmArea;

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
        if (!_scope.Enabled(c))
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

        foreach (var expired in _skipped.Where(s => s.Value <= c.Now).Select(s => s.Key).ToList())
            _skipped.Remove(expired);

        var near = w.GroundItems
            .Where(i => i.Kind == GroundItemKind.Resource && !_skipped.ContainsKey(i.Id) && _scope.InArea(c, i.Position))
            .ToList();
        // Нересурс (квестовый, особый) — только если явно в списке, и тогда без условий: ни инструмент, ни сумку, ни квест не проверяем
        var special = near.Where(i => i.Special && _scope.Listed(c, i.Name));
        var resource = special.Concat(Regular(c, near.Where(i => !i.Special)))
            .OrderBy(i => i.Distance)
            .FirstOrDefault();
        if (resource is null)
            return false;

        if (!resource.Special && w.BagFull)
            c.Say($"gather-into-stack-{resource.Id:X8}", $"Сумка полна, но добыча {resource.Name} ляжет в начатую стопку — копаю", seconds: 600);

        Status = $"копаю {resource.Name}";
        var sent = c.Send(new GatherAction(resource));
        if (sent == SubmitStatus.Sent)
            c.Log.Info($"Копаю {resource.Name}{_scope.Where(c)}, {resource.Distance:0.0} м");
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    // Обычные ресурсы: нужна кирка в сумке, список лута разрешает, при полной сумке — добыча ляжет в начатые стопки
    private IEnumerable<GroundItem> Regular(BrainContext c, IEnumerable<GroundItem> resources)
    {
        var w = c.World;
        var allowed = resources.Where(i => _scope.Wanted(c, i.Name)).ToList();
        if (allowed.Count == 0)
            return [];

        // Не дорос — игра не даст копать; уровень неизвестен (0) — не проверяем
        var level = w.Host.Level;
        foreach (var low in allowed.Where(i => level > 0 && i.Mine?.LevelRequired > level))
            c.Say($"gather-level-{low.Tid}", $"Копать не буду: {low.Name} — нужен {low.Mine!.LevelRequired} уровень, у персонажа {level}", seconds: 1800);
        allowed = allowed.Where(i => level <= 0 || !(i.Mine?.LevelRequired > level)).ToList();
        if (allowed.Count == 0)
            return [];

        if (tools.Count == 0)
        {
            c.Say("gather-no-tools", "Копать не буду: для этого сервера неизвестно, какой предмет — кирка", LogLevel.Warning, 600);
            return [];
        }

        if (!w.Inventory.Any(i => tools.Contains(i.Tid)))
        {
            c.Say("gather-no-pickaxe", "Копать не буду: нет кирки в сумке", LogLevel.Warning, 300);
            return [];
        }

        var fits = allowed.Where(w.FitsInBag).ToList();
        if (fits.Count == 0)
            c.Say("gather-bag-full", "Копать не буду: сумка полна, а добыча ресурсов рядом не ляжет в начатые стопки", LogLevel.Warning, 300);
        return fits;
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

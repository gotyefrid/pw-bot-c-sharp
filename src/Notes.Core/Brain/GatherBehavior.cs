using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Копать ресурсы: что и где — <see cref="IGatherScope"/> (фарм — в радиусе фарма по списку лута, обход — у точки по своему
/// списку). Стоит перед боем: пока рядом есть ресурсы из списка — копаем их подряд, потом мобы.
/// Бой и лут не перебиваем; напали на перса или пета — бросаем копание, бой убивает нападающего, потом копаем дальше.
/// Без инструмента (кирки) в сумке к ресурсам не подходим. Сумка полна — копаем только то, чья добыча (по справочнику игры,
/// <see cref="GroundItem.Mine"/>) целиком ляжет в начатые стопки. Ресурс выше уровня персонажа (по справочнику) не копаем. «Нересурсы» (<see cref="GroundItem.Special"/>: квестовые,
/// особые) — только если их название явно в списке, и тогда без всяких условий: просто пробуем копать.
/// Ресурс в зоне опасного моба (<see cref="IGatherScope.Guard"/>, у обхода) не копаем — проверяем каждый раз заново: моб мог уйти.
/// <para>
/// Обход в полёте (<see cref="IGatherScope.FromAbove"/>): к ресурсу ниже перса сначала летим сами на своей высоте точно над ним,
/// потом «копать» — игра ведёт к ресурсу по прямой, то есть вертикально вниз, мимо мобов вокруг. Путь (к точке над ресурсом и вниз)
/// тоже проверяем на зоны опасных мобов. Не долетели над ресурсом дважды (упёрлись) — копаем как раньше, путь выберет игра.
/// </para>
/// </summary>
public sealed class GatherBehavior(IReadOnlyCollection<uint> tools, IGatherScope? scope = null) : IBehavior
{
    private readonly IGatherScope _scope = scope ?? FarmAreaScope.Instance;

    // Не вышло (не дошли, копание не началось, сбили N раз подряд) — ресурс бросаем на время
    private static readonly TimeSpan SkipFor = TimeSpan.FromMinutes(1);
    private const int KnockdownsToSkip = 3;

    // Над ресурсом — ближе этого по горизонтали; ресурс ниже перса меньше чем на столько — подлетать сверху незачем
    private const float AboveTolerance = 2f;
    private const float MinDrop = 3f;
    // Столько раз не долетели над ресурсом — копаем без подлёта
    private const int AboveFailuresToGiveUp = 2;

    private readonly Dictionary<uint, DateTime> _skipped = [];

    // Какой шаг копания уже записан в лог — пишем каждый шаг один раз
    private GatherAction? _logged;
    private GatherPhase _loggedPhase;
    private readonly Dictionary<uint, int> _knockdowns = [];

    // Подлёт сверху: к какому ресурсу летим сейчас и сколько раз не долетели над каждым
    private GroundItem? _above;
    private readonly Dictionary<uint, int> _aboveFailures = [];

    public string Name => "сбор";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!_scope.Enabled(c))
            return false;

        var w = c.World;
        var pending = c.Mine.OfType<GatherAction>().FirstOrDefault();

        // Напали — ход бою; копание фоновое, его прервёт первое же действие боя (решает исполнитель)
        if (TargetSelector.Aggressor(w) is { } aggressor)
        {
            if (pending is not null)
                c.Say($"gather-attacked-{pending.Item.Id:X8}", $"{aggressor.Name} напал — бросаю копать {pending.Item.Name}", seconds: 600);
            return false;
        }

        if (pending is not null)
        {
            LogStep(c, pending);
            Status = Step(w, pending);
            return true;
        }

        // Летим над ресурсом: пока он на месте и путь чист — ждём; пропал (выкопали другие) или путь закрыл моб — бросаем
        if (c.Mine.OfType<MoveAction>().FirstOrDefault() is { } above && _above is { } target)
        {
            var now = w.GroundItems.FirstOrDefault(i => i.Id == target.Id);
            if (now is not null && Safe(c, now))
            {
                Status = $"лечу над {target.Name}, {w.Host.Position.HorizontalDistanceTo(target.Position):0} м";
                return true;
            }

            c.Forget(above);
            _above = null;
            if (now is null)
                c.Log.Info($"{target.Name} пропал, пока летел над ним");
        }

        // Идёт бой или лут — не перебиваем
        if (c.Fight.Busy)
            return false;

        foreach (var expired in _skipped.Where(s => s.Value <= c.Now).Select(s => s.Key).ToList())
            _skipped.Remove(expired);

        var near = w.GroundItems
            .Where(i => i.Kind == GroundItemKind.Resource && !_skipped.ContainsKey(i.Id) && _scope.InArea(c, i.Position))
            .ToList();
        // Нересурс (квестовый, особый) — только если явно в списке, и тогда без условий: ни инструмент, ни сумку, ни квест не проверяем
        var special = near.Where(i => i.Special && _scope.Listed(c, i.Name));
        var resource = special.Concat(Regular(c, near.Where(i => !i.Special)))
            .OrderBy(i => i.Offset.Direct)
            .FirstOrDefault(i => Safe(c, i));
        if (resource is null)
            return false;

        if (!resource.Special && w.BagFull)
            c.Say($"gather-into-stack-{resource.Id:X8}", $"Сумка полна, но добыча {resource.Name} ляжет в начатую стопку — копаю", seconds: 600);

        if (Over(c, resource) is { } over)
        {
            Status = $"лечу над {resource.Name}, {resource.Offset.Horizontal:0} м";
            var move = new MoveAction(over, AboveTolerance, fly: true) { Priority = ActionPriority.Background };
            var flown = c.Send(move);
            if (flown == SubmitStatus.Sent)
            {
                _above = resource;
                c.Log.Info($"Лечу над {resource.Name}{_scope.Where(c)}, {resource.Offset}");
            }
            return flown is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
        }

        Status = $"иду к {resource.Name}, {resource.Offset.Direct:0} м";
        var gather = new GatherAction(resource);
        var sent = c.Send(gather);
        if (sent == SubmitStatus.Sent)
        {
            c.Log.Info($"Бегу к {resource.Name}{_scope.Where(c)}, {resource.Offset}");
            (_logged, _loggedPhase) = (gather, GatherPhase.Approach);
        }
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    // Новый шаг копания — строка в лог: «Копаю … дошёл за», «Выкопал … жду добычу»
    private void LogStep(BrainContext c, GatherAction gather)
    {
        if (ReferenceEquals(_logged, gather) && _loggedPhase == gather.Phase)
            return;

        (_logged, _loggedPhase) = (gather, gather.Phase);
        var name = gather.Item.Name;
        switch (gather.Phase)
        {
            case GatherPhase.Digging:
                var bar = c.World.Host.Gather is { TotalMs: > 0 } g ? $", полоска {g.TotalMs / 1000.0:0} с" : "";
                c.Log.Info($"Копаю {name}: дошёл за {gather.WalkTime?.TotalSeconds:0.0} с{bar}");
                break;
            case GatherPhase.Dug:
                c.Log.Info($"Выкопал {name} за {gather.DigTime?.TotalSeconds:0.0} с, жду добычу");
                break;
        }
    }

    // Какой шаг копания идёт — для строки состояния
    private static string Step(WorldState w, GatherAction gather)
    {
        var name = gather.Item.Name;
        switch (gather.Phase)
        {
            case GatherPhase.Digging when w.Host.Gather is { TotalMs: > 0 } g:
                return $"копаю {name}, {g.ElapsedMs / 1000.0:0} из {g.TotalMs / 1000.0:0} с";
            case GatherPhase.Digging:
                return $"копаю {name}";
            case GatherPhase.Dug:
                return $"выкопал {name}, жду добычу";
            default:
                var item = w.GroundItems.FirstOrDefault(i => i.Id == gather.Item.Id) ?? gather.Item;
                return $"иду к {name}, {item.Offset.Direct:0} м";
        }
    }

    // Куда лететь сначала — на своей высоте над ресурсом; null — подлёт сверху не нужен (не обход в полёте, ресурс не ниже,
    // уже над ним, не долетали дважды) — сразу копать
    private Position? Over(BrainContext c, GroundItem item)
    {
        var host = c.World.Host.Position;
        if (!_scope.FromAbove(c) || item.Position.Height > host.Height - MinDrop
            || host.HorizontalDistanceTo(item.Position) <= AboveTolerance
            || (_aboveFailures.TryGetValue(item.Id, out var failures) && failures >= AboveFailuresToGiveUp))
            return null;
        return new Position(item.Position.X, host.Height, item.Position.Y);
    }

    // Не в зоне опасного моба, и путь к нему (сверху — над ним и вниз) не через зону; иначе — говорим (раз в 5 мин на ресурс) и не летим
    private bool Safe(BrainContext c, GroundItem item)
    {
        if (_scope.Guard(c, item.Position) is { } guard)
        {
            c.Say($"gather-danger-{item.Id:X8}",
                $"Не копаю {item.Name}: рядом опасный {guard.Name} (ур. {guard.Level}, агро {guard.AggroRadius} м) — " +
                $"{guard.Position.HorizontalDistanceTo(item.Position):0} м от ресурса", seconds: 300);
            return false;
        }

        if (!_scope.FromAbove(c))
            return true;

        // Сверху путь — на своей высоте к точке над ресурсом и вертикально вниз; без подлёта (уже над ним, не долетали) — прямо к нему
        var host = c.World.Host.Position;
        var onPath = Over(c, item) is { } top
            ? _scope.GuardOnPath(c, host, top) ?? _scope.GuardOnPath(c, top, item.Position)
            : _scope.GuardOnPath(c, host, item.Position);
        if (onPath is null)
            return true;

        c.Say($"gather-danger-path-{item.Id:X8}",
            $"Не копаю {item.Name}: по пути к нему опасный {onPath.Name} (ур. {onPath.Level}, агро {onPath.AggroRadius} м)", seconds: 300);
        return false;
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
        if (outcome.Action is MoveAction)
        {
            // Не долетели над ресурсом (упёрлись, сели) — ещё раз; дважды — копаем без подлёта, путь выберет игра
            if (outcome.Status == ActionStatus.Rejected && _above is { } above)
            {
                var failures = _aboveFailures[above.Id] = _aboveFailures.TryGetValue(above.Id, out var n) ? n + 1 : 1;
                if (failures >= AboveFailuresToGiveUp)
                    c.Log.Warning($"Не долетел над {above.Name} {failures} раза ({outcome.Details}) — копаю без подлёта сверху");
            }
            _above = null;
            return;
        }

        if (outcome.Action is not GatherAction gather)
            return;

        var id = gather.Item.Id;
        switch (outcome.Status)
        {
            case ActionStatus.Confirmed:
                _knockdowns.Remove(id);
                _aboveFailures.Remove(id);
                c.GatheredAt = c.Now;
                // Добыча пришла в том же снимке, где кончилась копка, — шага «жду добычу» не было, «выкопал» пишем здесь
                if (gather.DigTime is { } dig && !(ReferenceEquals(_logged, gather) && _loggedPhase == GatherPhase.Dug))
                    c.Log.Info($"Выкопал {gather.Item.Name} за {dig.TotalSeconds:0.0} с");
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
        _above = null;
        _aboveFailures.Clear();
        Status = null;
    }
}

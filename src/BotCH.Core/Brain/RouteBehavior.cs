using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Обход маршрута в режиме «Собирать ресурсы»: точки по порядку, после последней — снова первая. Сначала долетаем до
/// текущей точки (по дороге ничего не копаем), потом <see cref="GatherBehavior"/> (стоит раньше, со <see cref="Scope"/>) копает
/// то, что задано у этой точки, в радиусе от неё; напавших бьёт бой. Копать больше нечего — к следующей точке. Двигаемся так, как стоял перс при «Старт»: в воздухе — летим
/// на высоте точки (упал на землю — взлетаем), на земле — бежим с автопутём.
/// </summary>
public sealed class RouteBehavior(IReadOnlyCollection<uint> tools) : IBehavior
{
    // Дошли — ближе этого к точке по земле (высота не важна: в полёте точку могли записать у земли)
    private const float ArriveDistance = 5f;
    private const float MoveTolerance = 3f;
    // Столько раз подряд не дошли до точки (упёрлись) — пропускаем её
    private const int FailuresToSkip = 2;

    private int _index;
    // Долетели до текущей точки — теперь копаем у неё (сбрасывается при переходе к следующей)
    private bool _arrived;
    private bool? _inAir;
    private int _failures;

    public string Name => "обход";
    public string? Status { get; private set; }

    /// <summary>Номер текущей точки (с 0).</summary>
    public int Index => _index;

    /// <summary>Что и где копать при обходе: только долетев до текущей точки — в радиусе от неё то, что задано у этой точки.</summary>
    public GatherScope Scope => new(
        c => _arrived && c.Settings.Route.Points.Count > 0,
        (c, p) => Current(c) is { } point && p.HorizontalDistanceTo(point.Position) <= c.Settings.Route.Radius,
        (c, name) => Current(c)?.Wants(name) == true,
        (c, name) => Current(c)?.Lists(name) == true,
        c => Current(c) is { } point ? $" у точки {_index % c.Settings.Route.Points.Count + 1}/{c.Settings.Route.Points.Count} «{point.Name}»" : "");

    private RoutePoint? Current(BrainContext c)
    {
        var points = c.Settings.Route.Points;
        return points.Count == 0 ? null : points[_index % points.Count];
    }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var w = c.World;
        var route = c.Settings.Route;
        var points = route.Points;
        if (points.Count == 0)
            return c.RequestStop("маршрут пуст — добавьте точки на вкладке «Ресы»");
        if (tools.Count > 0 && !w.Inventory.Any(i => tools.Contains(i.Tid)))
            return c.RequestStop("нет кирки в сумке");

        if (_inAir is null)
        {
            _inAir = w.Host.Flying == true;
            c.Log.Info($"Обход: {points.Count} точек, {(_inAir.Value ? "в воздухе" : "по земле")}, ресурсы в {route.Radius} м от точки");
        }

        if (_index >= points.Count)
            _index = 0;
        var point = points[_index];
        if (_arrived)
        {
            // У точки копать больше нечего (иначе ход взяло бы копание); полная сумка и ресурсы не влезают — обходить дальше незачем
            if (w.BagFull && w.GroundItems.Any(i => Blocked(c, i)))
                return c.RequestStop("сумка полна — добыча ресурсов не помещается");

            Next(c, "здесь всё");
            point = points[_index];
        }
        else if (w.Host.Position.HorizontalDistanceTo(point.Position) <= ArriveDistance)
        {
            // Долетели: со следующего шага копаем у этой точки
            _arrived = true;
            c.Log.Info($"На точке {_index + 1}/{points.Count} «{point.Name}» — ищу: {point.Describe()}");
            Status = $"на точке {_index + 1}/{points.Count} «{point.Name}»";
            return true;
        }

        var distance = w.Host.Position.HorizontalDistanceTo(point.Position);
        var where = $"точке {_index + 1}/{points.Count} «{point.Name}» ({point.Describe()}), {distance:0} м";
        var pending = c.Runner.Pending.OfType<MoveAction>().FirstOrDefault(m => m.Priority == ActionPriority.Background);
        if (pending is not null && pending.Point == point.Position)
        {
            Status = (_inAir == true ? "лечу к " : "иду к ") + where;
            return true;
        }

        if (_inAir == true && w.Host.Flying == false)
        {
            Status = "взлетаю";
            return c.Submit(new FlyAction(up: true));
        }

        var move = _inAir == true
            ? new MoveAction(point.Position, MoveTolerance, fly: true) { Priority = ActionPriority.Background }
            : new MoveAction(point.Position, MoveTolerance, smart: true) { Priority = ActionPriority.Background };
        // Ещё долетаем до прошлой точки — сразу к новой
        var sent = pending is null ? c.Send(move) : c.Runner.Replace(move, w).Status;
        if (sent == SubmitStatus.Sent)
            c.Log.Info((_inAir == true ? "Лечу к " : "Иду к ") + where);
        Status = (_inAir == true ? "лечу к " : "иду к ") + where;
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    // Нужный обычный ресурс у точки, который не ляжет в сумку
    private bool Blocked(BrainContext c, GroundItem item)
        => item is { Kind: GroundItemKind.Resource, Special: false } && Scope.InArea(c, item.Position) && Scope.Wanted(c, item.Name)
           && !c.World.FitsInBag(item);

    private void Next(BrainContext c, string why)
    {
        var points = c.Settings.Route.Points;
        var was = points[_index];
        _index = (_index + 1) % points.Count;
        _arrived = false;
        _failures = 0;
        c.Log.Info($"Точка {points.IndexOf(was) + 1} «{was.Name}» — {why}; дальше {_index + 1}/{points.Count} «{points[_index].Name}»");
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is not MoveAction { Priority: ActionPriority.Background } move)
            return;
        if (outcome.Status == ActionStatus.Confirmed)
        {
            _failures = 0;
            return;
        }
        if (outcome.Status == ActionStatus.Cancelled || c.Settings.Route.Points.Count == 0
            || c.World.Host.Position.HorizontalDistanceTo(move.Point) <= ArriveDistance)
            return;

        _failures++;
        if (_failures < FailuresToSkip)
            return;
        c.Log.Warning($"Не дойти до точки: {outcome.Details}");
        Next(c, $"не дошли {FailuresToSkip} раза подряд, пропускаю");
    }

    public void Reset()
    {
        _index = 0;
        _arrived = false;
        _inAir = null;
        _failures = 0;
        Status = null;
    }
}

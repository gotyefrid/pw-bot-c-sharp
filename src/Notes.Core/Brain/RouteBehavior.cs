using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Обход маршрута в режиме «Собирать ресурсы»: точки по порядку, после последней — снова первая. Сначала долетаем до
/// текущей точки (по дороге ничего не копаем), потом <see cref="GatherBehavior"/> (стоит раньше; что и где копать, говорит
/// обход — <see cref="IGatherScope"/>) копает
/// то, что задано у этой точки, в радиусе от неё; напавших бьёт бой. Копать больше нечего — к следующей точке. Двигаемся так, как стоял перс при «Старт»: в воздухе — летим
/// на высоте точки (упал на землю — взлетаем; ниже точки больше чем на 3 м — сначала вертикально вверх на её высоту, потом
/// к ней), на земле — бежим с автопутём.
/// </summary>
/// <param name="start">С какой точки начинать (с 0) — выбранная в окне при «Старт»; не настройка, в файл не пишется.</param>
public sealed class RouteBehavior(IReadOnlyCollection<uint> tools, int start = 0) : IBehavior, IRouteProgress, IGatherScope
{
    // Дошли — ближе этого к точке по земле (высота не важна: в полёте точку могли записать у земли)
    private const float ArriveDistance = 5f;
    private const float MoveTolerance = 3f;
    // Столько раз подряд не дошли до точки (упёрлись) — пропускаем её
    private const int FailuresToSkip = 2;
    // В полёте ниже точки больше чем на столько — сначала подъём на её высоту
    private const float ClimbFirst = 3f;

    private int _index;
    // Долетели до текущей точки — теперь копаем у неё (сбрасывается при переходе к следующей)
    private bool _arrived;
    private bool? _inAir;
    private int _failures;
    // Последняя точка, до которой долетели (с 0); null — ещё ни одной
    private int? _lastVisited;
    // Вернуться к точке по-настоящему (с её высотой): после ухода вверх над ней «дошли по горизонтали» — неправда
    private bool _returning;
    // У точки всё выкопано — возвращаемся на неё, чтобы к следующей лететь от точки, а не от последнего ресурса
    private bool _leaving;
    // Подъём на высоту точки перед полётом к ней; не поднялись (потолок, скала) — до следующей точки летим к ней напрямую
    private MoveAction? _climb;
    private bool _climbFailed;
    // Какой маршрут обходим (номер из общего файла): сменили в окне на ходу — начинаем новый с первой точки
    private string? _routeId;

    public string Name => "обход";
    public string? Status { get; private set; }

    /// <summary>Номер текущей точки (с 0).</summary>
    public int Index => _index;

    // Что и где копать при обходе (IGatherScope): только долетев до текущей точки — в радиусе от неё то, что задано у этой точки
    public bool Enabled(BrainContext c) => _arrived && c.Settings.Route.Points.Count > 0;

    public bool InArea(BrainContext c, Position p)
        => Current(c) is { } point && p.HorizontalDistanceTo(point.Position) <= c.Settings.Route.Radius;

    public bool Wanted(BrainContext c, string name) => Current(c)?.Wants(name) == true;
    public bool Listed(BrainContext c, string name) => Current(c)?.Lists(name) == true;

    public string Where(BrainContext c)
        => c.Settings.Route.Points.Count > 0 ? $" у точки {_index % c.Settings.Route.Points.Count + 1}/{c.Settings.Route.Points.Count}" : "";

    public NpcInfo? Guard(BrainContext c, Position p) => DangerZones.Guard(c.World, p, c.Settings.Route);

    // Обход в полёте — к ресурсу сверху: по земле и в воде путь выбирает игра, как раньше
    public bool FromAbove(BrainContext c) => _inAir == true && c.World.Host.Flying == true;

    public NpcInfo? GuardOnPath(BrainContext c, Position from, Position to) => DangerZones.GuardOnPath(c.World, from, to, c.Settings.Route);

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
            _index = start >= 0 && start < points.Count ? start : 0;
            c.Log.Info($"Обход: {points.Count} точек, начинаю с {_index + 1}-й, {(_inAir.Value ? "в воздухе" : "по земле")}, "
                       + $"ресурсы в {route.Radius} м от точки");
        }

        if (_routeId is null)
            _routeId = route.RouteId;
        else if (_routeId != route.RouteId)
        {
            _routeId = route.RouteId;
            (_index, _arrived, _returning, _leaving, _failures, _lastVisited, _climbFailed) = (0, false, false, false, 0, null, false);
            c.Log.Info($"Маршрут сменился: {points.Count} точек, начинаю с 1-й");
        }

        if (_index >= points.Count)
            _index = 0;
        var point = points[_index];
        var pending = c.Mine.OfType<MoveAction>().FirstOrDefault();
        if (_arrived)
        {
            // Тело занято (подбор, каст, взлёт) — копание получило бы «занято»; это не «копать нечего», ждём
            if (c.Runner.BodyBusy(w) is { } busy)
            {
                Status = $"на точке {_index + 1}/{points.Count}: жду — {busy}";
                return true;
            }

            // У точки копать больше нечего (иначе ход взяло бы копание); полная сумка и ресурсы не влезают — обходить дальше незачем
            if (w.BagFull && w.GroundItems.Any(i => Blocked(c, i)))
                return c.RequestStop("сумка полна — добыча ресурсов не помещается");

            // Путь между точками — от точки к точке: отошли копать (в полёте — и по высоте) — сначала назад на точку
            var away = _inAir == true ? w.Host.Position.DistanceTo(point.Position) : w.Host.Position.HorizontalDistanceTo(point.Position);
            if (away > ArriveDistance)
            {
                _arrived = false;
                _returning = true;
                _leaving = true;
                c.Log.Info($"Точка {_index + 1} — здесь всё; возвращаюсь на неё, от неё — к следующей");
            }
            else
            {
                Next(c, "здесь всё");
                point = points[_index];
            }
        }
        else if (!_returning && w.Host.Position.HorizontalDistanceTo(point.Position) <= ArriveDistance && pending?.Point != point.Position)
        {
            if (_leaving)
            {
                // Вернулись на отработанную точку — сразу к следующей, без нового поиска
                Next(c, "вернулся на неё");
                point = points[_index];
            }
            else
            {
                // Долетели совсем (полёт к точке закончился, с высотой): со следующего шага копаем у этой точки
                _arrived = true;
                _lastVisited = _index;
                c.Log.Info($"На точке {_index + 1}/{points.Count} — ищу: {point.Describe()}");
                Status = $"на точке {_index + 1}/{points.Count}";
                return true;
            }
        }

        var distance = w.Host.Position.HorizontalDistanceTo(point.Position);
        var where = $"точке {_index + 1}/{points.Count} ({point.Describe()}), {distance:0} м";
        if (pending is not null && pending.Point == point.Position)
        {
            Status = (_inAir == true ? "лечу к " : "иду к ") + where;
            return true;
        }

        // Полётник сняли (или упал на землю без него) — дальше по земле: «Полёт» без полётника ничего не делает
        if (_inAir == true && w.Host.Flying == false && w.HasFlyer == false)
        {
            _inAir = false;
            c.Log.Warning("Полётника нет — обхожу дальше по земле");
        }

        if (_inAir == true && w.Host.Flying == false)
        {
            Status = "взлетаю";
            return c.Submit(new FlyAction(up: true));
        }

        // Ниже точки (после копки, ухода от моба) — сначала вертикально вверх на её высоту, потом к ней: не наискосок над мобами
        if (_inAir == true && !_climbFailed && w.Host.Position.Height < point.Position.Height - ClimbFirst && distance > ArriveDistance)
        {
            Status = $"поднимаюсь на высоту точки {_index + 1}/{points.Count}, {point.Position.Height - w.Host.Position.Height:0} м";
            if (pending is not null && ReferenceEquals(pending, _climb))
                return true;

            var climb = new MoveAction(new Position(w.Host.Position.X, point.Position.Height, w.Host.Position.Y), MoveTolerance, fly: true)
                { Priority = ActionPriority.Background };
            var climbing = pending is null ? c.Send(climb) : c.Replace(climb);
            if (climbing == SubmitStatus.Sent)
            {
                _climb = climb;
                c.Log.Info($"Поднимаюсь на высоту точки {_index + 1} (+{point.Position.Height - w.Host.Position.Height:0} м), потом к ней");
            }
            return climbing is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
        }

        // В воде — плывём в точку с её высотой: обычный ход держит глубину
        var move = _inAir == true
            ? new MoveAction(point.Position, MoveTolerance, fly: true) { Priority = ActionPriority.Background }
            : c.Swimming ? new MoveAction(point.Position, MoveTolerance, swim: true) { Priority = ActionPriority.Background }
            : new MoveAction(point.Position, MoveTolerance, smart: true) { Priority = ActionPriority.Background };
        // Ещё долетаем до прошлой точки — сразу к новой
        var sent = pending is null ? c.Send(move) : c.Replace(move);
        if (sent == SubmitStatus.Sent)
        {
            _returning = false;
            c.Log.Info((_inAir == true ? "Лечу к " : "Иду к ") + where);
        }
        Status = (_inAir == true ? "лечу к " : "иду к ") + where;
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    /// <summary>
    /// Куда вернуться после смерти (<see cref="DeathReturnBehavior"/>): к последней посещённой точке (ни одной — к текущей), на её
    /// высоте, если обходим в полёте; обход дальше — с неё, искать заново. Маршрут пуст — null (бот остановится).
    /// </summary>
    public Spot? AfterDeath(BrainContext c)
    {
        var points = c.Settings.Route.Points;
        if (points.Count == 0)
            return null;

        BackToLastVisited(c);
        var point = points[_index % points.Count];
        return new Spot(point.Position, _inAir == true ? TripEnd.Air : TripEnd.Ground);
    }

    /// <summary>
    /// Снова к последней посещённой точке и искать у неё заново (после ухода от опасного моба: он, скорее всего, отошёл).
    /// Ни одной ещё не было — к текущей.
    /// </summary>
    public void BackToLastVisited(BrainContext c)
    {
        var points = c.Settings.Route.Points;
        if (_lastVisited is int last && last < points.Count)
            _index = last;
        _arrived = false;
        _failures = 0;
        _returning = true;
        _leaving = false;
        _climbFailed = false;
    }

    // Нужный обычный ресурс у точки, который не ляжет в сумку
    private bool Blocked(BrainContext c, GroundItem item)
        => item is { Kind: GroundItemKind.Resource, Special: false } && InArea(c, item.Position) && Wanted(c, item.Name)
           && !c.World.FitsInBag(item);

    private void Next(BrainContext c, string why)
    {
        var points = c.Settings.Route.Points;
        var was = _index;
        _index = (_index + 1) % points.Count;
        _arrived = false;
        _leaving = false;
        _failures = 0;
        _climbFailed = false;
        c.Log.Info($"Точка {was + 1} — {why}; дальше {_index + 1}/{points.Count}");
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is not MoveAction move)
            return;
        if (ReferenceEquals(move, _climb))
        {
            _climb = null;
            // Подъём не дошёл — это не «не дойти до точки»: летим к ней напрямую
            if (outcome.Status == ActionStatus.Rejected)
            {
                _climbFailed = true;
                c.Log.Warning($"Не поднялся на высоту точки ({outcome.Details}) — лечу к ней напрямую");
            }
            return;
        }
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
        _lastVisited = null;
        _returning = false;
        _leaving = false;
        _climb = null;
        _climbFailed = false;
        _routeId = null;
        Status = null;
    }
}

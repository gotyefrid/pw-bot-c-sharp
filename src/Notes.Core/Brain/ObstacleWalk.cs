using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Navigation;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Обход препятствий до цели (моб, лут, пет): если прямую до неё загораживает камень, дом или дерево, ведём перса своим путём
/// (<see cref="PathFinder"/>) короткими «бежать по прямой», пока прямая не освободится, — дальше как раньше: скилл «как кнопкой»
/// и подбор «как мышкой» сами добегают по прямой. Прямая свободна, препятствий для места не знаем (<see cref="BrainContext.Obstacles"/>),
/// перс в воздухе или в воде, цель дальше <see cref="ObstacleMap.MaxRange"/> — обход не нужен, всё как раньше.
/// <para>
/// Один на поведение: действия ходьбы — его. Проверено 09.11 на 1.3.6 через Probe walk: двор за забором, моб за деревом; здесь —
/// то же: повтор команды, если перс встал сразу после точки пути (игра её пропускает), и не срезать поворот у угла.
/// </para>
/// </summary>
public sealed class ObstacleWalk
{
    // Цель сдвинулась дальше — путь заново
    private const float GoalMoved = 3f;

    // Дошли до точки пути; раньше (ближе Early) — к следующей, только если до неё уже видно с запасом
    private const float Reached = 0.3f;
    private const float Early = 0.8f;

    // Стоит — команду ещё раз, стоит и после — путь заново, и так не больше MaxReplans раз за обход
    private static readonly TimeSpan StandPatience = TimeSpan.FromSeconds(1.5);
    private const float StandMove = 0.3f;
    private const int MaxReplans = 3;

    private PathResult? _path;
    private PathPoint _goal;
    private int _next;
    private MoveAction? _move;
    private PathPoint _lastPlace;
    private DateTime _movedAt;
    private bool _resent;
    private ActionPriority _priority = ActionPriority.Normal;
    private int _replans;

    /// <summary>Идём ли сейчас в обход (для показа).</summary>
    public bool Active => _path is not null;

    /// <summary>
    /// Ход обхода к <paramref name="target"/>: true — идём в обход (ход занят); false — обход не нужен или закончен (прямая
    /// до цели свободна) — делать как раньше.
    /// </summary>
    /// <param name="priority">Важность шагов: у копания фоновая — бой, если нападут, должен их перебить.</param>
    /// <param name="within">Цель не дальше этого (радиус скилла с запасом) — обходить незачем: скилл бьёт сквозь стены и камни.
    /// 0 — радиуса нет (подбор, копание, удар мечом): обход, пока прямая не откроется.</param>
    public bool Step(BrainContext c, Position target, string what, ActionPriority priority = ActionPriority.Normal, float within = 0)
    {
        _priority = priority;
        if (within > 0 && c.World.Host.Position.DistanceTo(target) <= within)
        {
            if (_path is not null)
                c.Log.Debug($"Обход до {what}: в радиусе скилла ({within:0.0} м) — дальше как обычно");
            return Stop(c);
        }

        var w = c.World;
        var me = w.Host.Position;
        var here = new PathPoint(me.X, me.Y);
        var goal = new PathPoint(target.X, target.Y);
        if (c.Obstacles is null || w.Host.Flying == true || w.Host.InWater == true || here.DistanceTo(goal) > ObstacleMap.MaxRange
            || c.Obstacles.Around(me) is not { } area)
            return Stop(c);

        var tight = new PathFinder(area.Tight);
        var comfort = new PathFinder(area.Comfort);
        if (_path is null || _goal.DistanceTo(goal) > GoalMoved)
        {
            if (tight.LineFree(here, goal))
                return Stop(c);
            if (!Plan(c, here, goal, what, area))
                return Stop(c);
        }

        // Прямая до цели (или до свободного места у неё, если цель у самого камня) открылась — дальше как раньше
        var end = _path!.Points[_path.Points.Count - 1];
        if (comfort.LineFree(here, end) || tight.LineFree(here, goal))
        {
            c.Log.Debug($"Обход до {what}: прямая свободна, {here.DistanceTo(goal):0.0} м — дальше как обычно");
            return Stop(c);
        }

        while (_next < _path.Points.Count - 1)
        {
            var left = here.DistanceTo(_path.Points[_next]);
            if (left > Early || left > Reached && !comfort.LineFree(here, _path.Points[_next + 1]))
                break;
            _next++;
        }

        return Walk(c, me, here, _path.Points[_next], what, area);
    }

    /// <summary>Бросить обход (цель сменилась, бой кончился): своё действие ходьбы забываем.</summary>
    public bool Stop(BrainContext c)
    {
        if (_move is not null && c.Mine.Contains(_move))
            c.Forget(_move);
        _move = null;
        _path = null;
        _replans = 0;
        return false;
    }

    /// <summary>Сброс без контекста (бот остановлен): действия ходьбы исполнитель сбросит сам.</summary>
    public void Reset()
    {
        _move = null;
        _path = null;
        _replans = 0;
    }

    private bool Plan(BrainContext c, PathPoint here, PathPoint goal, string what, (IWalkArea Tight, IWalkArea Comfort) area)
    {
        var path = PathFinder.FindWithMargin(area.Comfort, area.Tight, here, goal);
        if (!path.Found)
        {
            c.Say($"obstacle-walk:{what}", $"Обход до {what}: пути нет — {path.Problem}; иду как обычно", LogLevel.Info, 30);
            return false;
        }

        c.Log.Info($"Обход до {what}: прямую загораживает препятствие — путь {path.Points.Count - 1} отрезков, {path.Length:0} м"
                   + (path.Tight ? " (впритирку)" : ""));
        _path = path;
        _goal = goal;
        _next = 1;
        _move = null;
        return true;
    }

    private bool Walk(BrainContext c, Position me, PathPoint here, PathPoint point, string what, (IWalkArea Tight, IWalkArea Comfort) area)
    {
        var mine = _move is not null && c.Mine.Contains(_move);
        if (mine && new PathPoint(_move!.Point.X, _move.Point.Y).DistanceTo(point) < 0.05f)
        {
            if (here.DistanceTo(_lastPlace) >= StandMove)
            {
                (_lastPlace, _movedAt) = (here, c.Now);
                return true;
            }

            if (c.Now - _movedAt < StandPatience)
                return true;

            // Стоит: игра могла пропустить команду (так бывает сразу после точки пути) — ещё раз; стоит и после — путь заново
            if (!_resent)
            {
                _resent = true;
                c.Forget(_move);
                return Send(c, me, point, mine: false);
            }

            if (++_replans > MaxReplans)
            {
                c.Say($"obstacle-stuck:{what}", $"Обход до {what}: застреваю — иду как обычно", LogLevel.Warning, 30);
                Stop(c);
                return false;
            }

            c.Log.Info($"Обход до {what}: застрял в {here}, путь заново");
            c.Forget(_move);
            var goal = _goal;
            _path = null;
            return Plan(c, here, goal, what, area) ? Send(c, me, _path!.Points[_next], mine: false) : Stop(c);
        }

        _resent = false;
        return Send(c, me, point, mine);
    }

    private bool Send(BrainContext c, Position me, PathPoint point, bool mine)
    {
        var move = new MoveAction(new Position(point.X, me.Height, point.Y), Reached) { Priority = _priority };
        var status = mine ? c.Replace(move) : c.Send(move);
        if (status is SubmitStatus.Sent or SubmitStatus.AlreadyPending)
        {
            _move = move;
            (_lastPlace, _movedAt) = (new PathPoint(me.X, me.Y), c.Now);
        }

        // Тело занято (каст, копка) — ход всё равно наш: ждём, не жмём скилл в обход препятствия
        return true;
    }
}

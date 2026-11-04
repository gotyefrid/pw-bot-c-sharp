using System;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>Чем кончить путь над точкой.</summary>
public enum TripEnd
{
    /// <summary>Сесть на землю.</summary>
    Ground,
    /// <summary>Не садиться — зависнуть на высоте точки.</summary>
    Air,
    /// <summary>Сесть в воду и нырнуть до высоты точки.</summary>
    Water,
}

/// <summary>Куда добраться и чем кончить путь.</summary>
public readonly record struct Spot(Position Position, TripEnd End);

public enum TripStep
{
    /// <summary>В пути: ход занят.</summary>
    Going,
    Arrived,
    /// <summary>Не добрались — причина в <see cref="FlightTrip.Failed"/>.</summary>
    Failed,
}

/// <summary>
/// Долгий путь до точки (из города на место фарма): взлёт, подъём строго вверх до высоты полёта (подъём по пути может врезаться в
/// дом или гору), отрезки по прямой по <see cref="LegLength"/>, над точкой — как велит <see cref="TripEnd"/>: сесть, зависнуть на
/// высоте точки или нырнуть до неё. Садимся, только когда остановились над точкой (<see cref="StillMove"/>, не дольше
/// <see cref="SettleMax"/>): с хода игра опускает по дуге, с инерцией, а не отвесно вниз. Не долетели — выше на <see cref="FailClimb"/>; не долетели <see cref="FlightFails"/> раз или полёта
/// в точку у сервера нет — пешком автопутём. Полётника нет (<see cref="WorldState.HasFlyer"/>) — сразу пешком, «Полёт» не жмём; надет —
/// не взлетели, жмём снова через <see cref="TakeoffRetry"/>, сколько угодно раз. Не знаем, надет ли, — не взлетели, идём пешком.
/// Не поведение, а деталь поведения: шаги — из его Tick, итоги — из его OnOutcome; действия уходят от его имени.
/// </summary>
public sealed class FlightTrip
{
    /// <summary>Над точкой — ближе этого по горизонтали (полёт останавливается в ~3 м от точки отрезка).</summary>
    public const float Arrived = 5f;

    /// <summary>Отрезок полёта: дальше за раз не летим (полёт в точку на сотни метров игра ведёт неровно).</summary>
    public const float LegLength = 150f;

    // Высоты — как в памяти (в 10 раз больше, чем в игре): 20 — 2 м
    public const float FailClimb = 20f;
    public const int FlightFails = 5;
    public static readonly TimeSpan TakeoffRetry = TimeSpan.FromSeconds(3);

    /// <summary>Между двумя шагами сдвинулись меньше этого (по горизонтали) — стоим над точкой, можно садиться.</summary>
    public const float StillMove = 0.3f;

    /// <summary>Не остановились над точкой за столько — садимся всё равно (не висеть вечно).</summary>
    public static readonly TimeSpan SettleMax = TimeSpan.FromSeconds(3);
    // Над городом бывает выше заданной высоты: во время полёта ниже неё не опускаемся, но и не выше этого запаса
    private const float KeepHigher = 50f;

    private readonly TripEnd _end;
    private float _cruise;
    private bool _started;
    private bool _climbed;
    private bool _walk;
    private bool _landFailed;
    private bool _diveFailed;
    private int _fails;
    private int _takeoffFails;
    private DateTime _takeoffAt = DateTime.MinValue;
    // Где были на прошлом шаге и с какого времени висим над точкой: садимся после остановки
    private Position? _last;
    private DateTime? _overSince;

    /// <param name="cruise">Высота полёта, как в памяти. Зависнуть в воздухе — не ниже высоты точки.</param>
    public FlightTrip(Spot to, float cruise)
    {
        To = to.Position;
        _end = to.End;
        _cruise = Math.Max(cruise, _end == TripEnd.Air ? To.Height : 0);
    }

    public Position To { get; }

    /// <summary>
    /// Точка в воздухе, а персонаж не летит (и полётник не «точно нет»): даже рядом с ней надо взлететь и подняться до её
    /// высоты, а не стоять на земле.
    /// </summary>
    public static bool MustTakeOff(BrainContext c, Spot to)
        => to.End == TripEnd.Air && c.World.Host.Flying != true && c.World.HasFlyer != false && c.Runner.Capabilities.Has(Capability.FlyTo);

    /// <summary>Почему не добрались; null — в пути или на месте.</summary>
    public string? Failed { get; private set; }

    public TripStep Step(BrainContext c)
    {
        if (Failed is not null)
            return TripStep.Failed;
        if (!_started)
        {
            _started = true;
            if (c.Runner.Capabilities.WhyNot(Capability.FlyTo) is { } why)
            {
                c.Log.Info($"Лететь не умею ({why}) — иду пешком");
                _walk = true;
            }
            else if (c.World.HasFlyer == false && c.World.Host.Flying != true)
            {
                c.Log.Info("Полётника нет — иду пешком");
                _walk = true;
            }
        }
        var host = c.World.Host;
        var here = host.Position;
        var last = _last;
        _last = here;
        if (c.Mine.Count > 0)
            return TripStep.Going;

        var left = here.HorizontalDistanceTo(To);
        var air = _end == TripEnd.Air && !_walk;
        if (left <= Arrived && (!air || Math.Abs(here.Height - To.Height) <= Arrived))
        {
            // Зависнуть можно только в полёте: на земле над точкой — взлететь
            if (air && host.Flying != true)
                c.Submit(new FlyAction(up: true));
            if (air)
                return host.Flying == true ? TripStep.Arrived : TripStep.Going;
            if (host.Flying == true && !_landFailed)
            {
                _overSince ??= c.Now;
                var still = last is { } was && was.HorizontalDistanceTo(here) < StillMove;
                if (still || c.Now - _overSince >= SettleMax)
                    c.Submit(new FlyAction(up: false));
                return TripStep.Going;
            }
            // Сели в воду — нырнуть до высоты точки: обычный ход держит глубину
            if (_end == TripEnd.Water && host.InWater == true && !_diveFailed && Math.Abs(here.Height - To.Height) > Arrived)
            {
                c.Submit(new MoveAction(To, tolerance: 2f, swim: true));
                return TripStep.Going;
            }
            return TripStep.Arrived;
        }

        if (_walk)
            c.Submit(new MoveAction(To, tolerance: 3f, smart: true));
        else if (host.Flying != true)
        {
            if (c.Now >= _takeoffAt)
                c.Submit(new FlyAction(up: true));
        }
        else if (!_climbed && here.Height < _cruise - 2)
        {
            _climbed = true;
            c.Submit(new MoveAction(here with { Height = _cruise }, tolerance: 2f, fly: true));
        }
        else
        {
            _climbed = true;
            c.Submit(Leg(here, left));
        }
        return TripStep.Going;
    }

    private MoveAction Leg(Position here, float left)
    {
        if (left <= Arrived)
            return new MoveAction(To, tolerance: 2f, fly: true); // в воздухе — опуститься на высоту точки
        var k = Math.Min(1f, LegLength / left);
        var height = Math.Max(_cruise, Math.Min(here.Height, _cruise + KeepHigher));
        var to = left <= LegLength && _end == TripEnd.Air ? To with { Height = height }
            : new Position(here.X + (To.X - here.X) * k, height, here.Y + (To.Y - here.Y) * k);
        return new MoveAction(to, tolerance: 3f, fly: true);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Status is ActionStatus.Confirmed or ActionStatus.Cancelled)
            return;

        switch (outcome.Action)
        {
            // Полётник надет — пробуем снова (сидел, каст, полёт здесь на время нельзя): пешком до места далеко
            case FlyAction { Up: true } when c.World.HasFlyer == true:
                _takeoffAt = c.Now + TakeoffRetry;
                if (++_takeoffFails == 1 || _takeoffFails % 10 == 0)
                    c.Log.Warning($"Не взлетел ({outcome.Details}) — полётник надет, пробую снова");
                break;
            // Не знаем, надет ли полётник: нет крыльев или полёт здесь запрещён
            case FlyAction { Up: true }:
                c.Log.Warning("Не взлетел — иду пешком");
                _walk = true;
                break;
            case FlyAction:
                _landFailed = true;
                break;
            case MoveAction { Fly: true } when ++_fails > FlightFails:
                c.Log.Warning("Не долетел — дальше пешком");
                _walk = true;
                break;
            case MoveAction { Fly: true }:
                _cruise += FailClimb;
                c.Log.Info($"Не долетел — поднимаюсь до {_cruise / 10:0.0}");
                break;
            case MoveAction { Swim: true }:
                _diveFailed = true;
                break;
            case MoveAction:
                Failed = "не дошёл до места";
                break;
        }
    }
}

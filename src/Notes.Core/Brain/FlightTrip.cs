using System;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Brain;

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
/// дом или гору), отрезки по прямой по <see cref="LegLength"/>, над точкой — посадка; <c>stayInAir</c> — не садиться, а опуститься
/// на высоту точки. Не долетели — выше на <see cref="FailClimb"/>; не взлетели, не долетели <see cref="FlightFails"/> раз или полёта
/// в точку у сервера нет — пешком автопутём.
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
    // Над городом бывает выше заданной высоты: во время полёта ниже неё не опускаемся, но и не выше этого запаса
    private const float KeepHigher = 50f;

    private readonly bool _stayInAir;
    private float _cruise;
    private bool _started;
    private bool _climbed;
    private bool _walk;
    private bool _landFailed;
    private int _fails;

    /// <param name="cruise">Высота полёта, как в памяти. В воздухе (<paramref name="stayInAir"/>) — не ниже высоты точки.</param>
    public FlightTrip(Position to, float cruise, bool stayInAir)
    {
        To = to;
        _stayInAir = stayInAir;
        _cruise = Math.Max(cruise, stayInAir ? to.Height : 0);
    }

    public Position To { get; }

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
        }
        if (c.Mine.Count > 0)
            return TripStep.Going;

        var host = c.World.Host;
        var here = host.Position;
        var left = here.HorizontalDistanceTo(To);
        if (left <= Arrived && (_walk || !_stayInAir || Math.Abs(here.Height - To.Height) <= Arrived))
        {
            if (_stayInAir || host.Flying != true || _landFailed)
                return TripStep.Arrived;
            c.Submit(new FlyAction(up: false));
            return TripStep.Going;
        }

        if (_walk)
            c.Submit(new MoveAction(To, tolerance: 3f, smart: true));
        else if (host.Flying != true)
            c.Submit(new FlyAction(up: true));
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
        var to = left <= LegLength && _stayInAir ? To with { Height = height }
            : new Position(here.X + (To.X - here.X) * k, height, here.Y + (To.Y - here.Y) * k);
        return new MoveAction(to, tolerance: 3f, fly: true);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Status is ActionStatus.Confirmed or ActionStatus.Cancelled)
            return;

        switch (outcome.Action)
        {
            // Нет крыльев или полёт здесь запрещён
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
            case MoveAction:
                Failed = "не дошёл до места";
                break;
        }
    }
}

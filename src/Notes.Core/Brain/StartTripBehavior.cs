using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;

namespace Notes.Core.Brain;

/// <summary>
/// «Старт» не в центре фарма — сначала долететь до него (<see cref="FlightTrip"/> на высоте <see cref="TargetSettings.TravelHeight"/>),
/// как бы близко ни был: с полётом — всегда; без полёта — автопутём пешком, только если центр дальше
/// <see cref="TargetSettings.TravelFrom"/> (0 — никуда не летим и не идём). Прилетев — как стоял перс, когда точку запоминали:
/// центр на земле — сесть (и дождаться, пока упадёт), в воздухе — взлететь и зависнуть на его высоте, в воде — нырнуть. Только раз за «Старт». Пора сливать опыт
/// (<see cref="ExpDrainBehavior.Due"/>) и задана точка для смертей — летим сразу на неё: на фарм слив вернёт сам.
/// <list type="bullet">
/// <item>Стоит раньше слива опыта: решает первым, лететь ли, — слив начнётся или на месте сразу, или долетев (а не в воздухе
/// на полпути и не после боя, который успел начаться, пока решали). Пока летим, ход у перелёта: банки, бой, лут и пет ждут.</item>
/// <item>Центр — точка старта: уже на месте, никуда не летим.</item>
/// </list>
/// </summary>
public sealed class StartTripBehavior : IBehavior, IMultiStepBehavior
{
    /// <summary>Дальше — видимо, точка из другой локации: не летим.</summary>
    public const float MaxTrip = DeathReturnBehavior.MaxTrip;

    private bool _checked;
    private FlightTrip? _trip;

    public string Name => "к центру фарма";
    public string? Status { get; private set; }
    public string? InMiddle => _trip is null ? null : "лечу на место";

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!_checked)
        {
            _checked = true;
            _trip = Plan(c);
            if (_trip is null)
                return false;
        }
        if (_trip is null)
            return false;

        var left = c.World.Host.Position.HorizontalDistanceTo(_trip.To);
        Status = $"лечу на место, {left:0} м";
        switch (_trip.Step(c))
        {
            case TripStep.Going:
                return true;
            case TripStep.Arrived:
                c.Log.Info("Долетел");
                break;
            default:
                c.Log.Warning($"Не добрался: {_trip.Failed} — остаюсь здесь");
                break;
        }
        _trip = null;
        Status = null;
        return false;
    }

    private static FlightTrip? Plan(BrainContext c)
    {
        var target = c.Settings.Target;
        if (target.TravelFrom <= 0 || !c.Runner.Capabilities.Has(Capability.Move))
            return null;

        // Пора сливать опыт и есть точка для смертей — сразу туда: на фарм всё равно вернёмся после слива
        Spot spot;
        string where;
        if (ExpDrainBehavior.Due(c) && ExpDrainBehavior.DeathSpot(c) is { } death)
            (spot, where) = (death, $"Опыта больше порога — точка для смертей «{c.Settings.ExpDrain.DeathPoint}»");
        else if (target.SelectedFarmPoint is { } point)
            (spot, where) = (new Spot(point.Position, BrainContext.SpotEnd(point)), $"Центр фарма «{point.Name}»");
        else
            return null;

        var distance = c.World.Host.Position.HorizontalDistanceTo(spot.Position);
        // С полётом — всегда, если уже не на месте (в воздухе над центром на земле — сесть, на земле под центром в воздухе — взлететь);
        // пешком — только издалека
        if (FlightTrip.CanFly(c) ? FlightTrip.There(c, spot) : distance <= target.TravelFrom)
            return null;
        if (distance > MaxTrip)
        {
            c.Log.Warning($"{where} в {distance:0} м — слишком далеко, не лечу");
            return null;
        }

        c.Log.Info($"{where} в {distance:0} м — лечу туда на высоте {target.TravelHeight:0}");
        return new FlightTrip(spot, target.TravelHeight * 10);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => _trip?.OnOutcome(c, outcome);

    public void Reset()
    {
        _checked = false;
        _trip = null;
        Status = null;
    }
}

using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;

namespace Notes.Core.Brain;

/// <summary>
/// «Старт» вдали от центра фарма (сохранённая точка дальше <see cref="TargetSettings.TravelFrom"/>) — сначала долететь до него
/// (<see cref="FlightTrip"/> на высоте <see cref="TargetSettings.TravelHeight"/>; без полёта — автопутём пешком). Прилетев — как
/// стоял перс, когда точку запоминали: сесть, зависнуть в воздухе или нырнуть. Только раз за «Старт».
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
    public string? InMiddle => _trip is null ? null : "лечу к центру фарма";

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
        Status = $"лечу к центру фарма, {left:0} м";
        switch (_trip.Step(c))
        {
            case TripStep.Going:
                return true;
            case TripStep.Arrived:
                c.Log.Info("Долетел до центра фарма — фармлю");
                break;
            default:
                c.Log.Warning($"До центра фарма не добрался: {_trip.Failed} — фармлю здесь");
                break;
        }
        _trip = null;
        Status = null;
        return false;
    }

    private static FlightTrip? Plan(BrainContext c)
    {
        var target = c.Settings.Target;
        if (target.TravelFrom <= 0 || target.SelectedFarmPoint is not { } point || !c.Runner.Capabilities.Has(Capability.Move))
            return null;

        var distance = c.World.Host.Position.HorizontalDistanceTo(point.Position);
        if (distance <= target.TravelFrom)
            return null;
        if (distance > MaxTrip)
        {
            c.Log.Warning($"Центр фарма «{point.Name}» в {distance:0} м — слишком далеко, не лечу");
            return null;
        }

        var end = point.Flying ? TripEnd.Air : point.InWater ? TripEnd.Water : TripEnd.Ground;
        c.Log.Info($"Центр фарма «{point.Name}» в {distance:0} м — лечу туда на высоте {target.TravelHeight:0}");
        return new FlightTrip(new Spot(point.Position, end), target.TravelHeight * 10);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => _trip?.OnOutcome(c, outcome);

    public void Reset()
    {
        _checked = false;
        _trip = null;
        Status = null;
    }
}

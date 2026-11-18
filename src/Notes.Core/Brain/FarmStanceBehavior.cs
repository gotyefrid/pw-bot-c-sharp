using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;

namespace Notes.Core.Brain;

/// <summary>
/// Поза у фарма — как стоял перс, когда точку фарма запоминали (<see cref="BrainContext.FarmSpot"/>): точка на земле, а перс в радиусе
/// фарма летит (взлетел сам, руками, или его оставил в воздухе кто-то из поведений) — садимся сразу, даже в бою: снизу бьёт и лутает
/// по-другому. Точка в воздухе, а перс стоит на земле — взлетаем, но только когда делать нечего <see cref="IdleBeforeTakeoff"/>: на
/// земле под центром копают, а бой с земли лучше не рвать взлётом; к высоте центра потом поднимает <see cref="ReturnBehavior"/>.
/// Только для сохранённой точки фарма: у точки старта позы нет. Вне радиуса, в воде, без полётника — не трогаем.
/// В наборе — после лечения, перед петом (пет зовётся по позе) и копкой.
/// </summary>
public sealed class FarmStanceBehavior : IBehavior
{
    /// <summary>Перс на земле у центра в воздухе, делать нечего столько — взлетаем.</summary>
    public static readonly TimeSpan IdleBeforeTakeoff = ReturnBehavior.IdleInside;

    // Не сели / не взлетели — не жать «Полёт» каждый шаг
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);
    // Шаги мозга — раз в ~0.25 с; пропуск дольше — ход забирал кто-то другой, «нечего делать» начинается заново
    private static readonly TimeSpan TickGap = TimeSpan.FromSeconds(0.6);

    private DateTime _nextTry = DateTime.MinValue;
    private DateTime _idleSince = DateTime.MinValue;
    private DateTime _lastIdle = DateTime.MinValue;
    // Сами взлетели к центру в воздухе — следом подняться к нему (иначе возврат ждал бы ещё раз «нечего делать»)
    private bool _climb;

    public string Name => "поза у фарма";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (c.Mine.Any(a => a is FlyAction))
        {
            Status = "меняю позу у фарма";
            return true;
        }

        var w = c.World;
        if (c.Settings.Target.SelectedFarmPoint is null || c.FarmSpot is not { } spot || spot.End == TripEnd.Water
            || w.Host.InWater == true || !c.InFarmArea(w.Host.Position) || c.Now < _nextTry)
            return false;

        if (_climb && spot.End == TripEnd.Air && w.Host.Flying == true)
        {
            _climb = false;
            if (w.Host.Position.Height < spot.Position.Height - ReturnBehavior.NearCenter
                && c.Send(new MoveAction(spot.Position, tolerance: 2f, fly: true) { Priority = ActionPriority.Background }) == SubmitStatus.Sent)
            {
                c.Log.Info($"Взлетел — поднимаюсь к центру фарма, {spot.Position.Height - w.Host.Position.Height:0} м вверх");
                return true;
            }
        }

        if (spot.End == TripEnd.Ground && w.Host.Flying == true)
            return Send(c, up: false, "Центр фарма на земле, а перс в воздухе — сажусь");

        if (spot.End != TripEnd.Air || w.Host.Flying != false || w.HasFlyer == false || !c.Runner.Capabilities.Has(Capability.FlyTo))
            return false;

        // На земле у центра в воздухе: ждём, пока делать нечего (не бой, не копка, не каст)
        if (c.Fight.Busy || c.Mine.Count > 0 || c.Runner.BodyBusy(w) is not null)
        {
            _lastIdle = DateTime.MinValue;
            return false;
        }
        if (c.Now - _lastIdle > TickGap)
            _idleSince = c.Now;
        _lastIdle = c.Now;
        if (c.Now - _idleSince < IdleBeforeTakeoff)
            return false;
        return Send(c, up: true, "Центр фарма в воздухе, а перс на земле — взлетаю");
    }

    private bool Send(BrainContext c, bool up, string why)
    {
        var sent = c.Send(new FlyAction(up));
        if (sent == SubmitStatus.Sent)
            _climb = up;
        if (sent == SubmitStatus.Sent)
            c.Say(up ? "stance-up" : "stance-down", why, seconds: 60);
        Status = up ? "взлетаю — центр фарма в воздухе" : "сажусь — центр фарма на земле";
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is FlyAction && outcome.Status is ActionStatus.Rejected or ActionStatus.Timeout or ActionStatus.Failed)
            _nextTry = c.Now + RetryAfter;
    }

    public void Reset()
    {
        _nextTry = DateTime.MinValue;
        _idleSince = _lastIdle = DateTime.MinValue;
        _climb = false;
        Status = null;
    }
}

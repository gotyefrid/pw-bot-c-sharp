using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;

namespace Notes.Core.Brain;

/// <summary>
/// Поза у фарма — как стоял перс, когда точку фарма запоминали (<see cref="BrainContext.FarmSpot"/>). Проверяем только перед новой
/// целью, когда боя нет: точка на земле, а перс в радиусе фарма летит — садимся; точка в воздухе, а перс на земле — взлетаем (не сразу
/// после копки, <see cref="AfterDig"/>) и поднимаемся к центру. Пока поза не та — ход держим: пет и новая цель ждут. В бою позу не трогаем.
/// Только для сохранённой точки фарма: у точки старта позы нет. Вне радиуса, в воде, без полётника — не трогаем.
/// В наборе — после лечения, перед петом (пет зовётся по позе) и боем.
/// </summary>
public sealed class FarmStanceBehavior : IBehavior
{
    /// <summary>Выкопали ресурс — столько не взлетаем: рядом может быть следующий.</summary>
    public static readonly TimeSpan AfterDig = TimeSpan.FromSeconds(10);

    // Не сели / не взлетели — не жать «Полёт» каждый шаг
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private DateTime _nextTry = DateTime.MinValue;
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

        // В бою позу не трогаем — только перед новой целью
        if (c.Fight.Busy)
            return false;

        if (spot.End == TripEnd.Ground && w.Host.Flying == true)
            return Send(c, up: false, "Центр фарма на земле, а перс в воздухе — сажусь");

        if (spot.End != TripEnd.Air || w.Host.Flying != false || w.HasFlyer == false || !c.Runner.Capabilities.Has(Capability.FlyTo))
            return false;

        // Сразу после копки не взлетаем: под центром копают по нескольку ресурсов подряд
        if (c.GatheredAt is { } dug && c.Now - dug < AfterDig)
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
        // Тело занято (докастовывает, лутает) — всё равно держим ход: новую цель — только в нужной позе
        return sent is not SubmitStatus.Failed;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is FlyAction && outcome.Status is ActionStatus.Rejected or ActionStatus.Timeout or ActionStatus.Failed)
            _nextTry = c.Now + RetryAfter;
    }

    public void Reset()
    {
        _nextTry = DateTime.MinValue;
        _climb = false;
        Status = null;
    }
}

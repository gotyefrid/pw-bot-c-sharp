using System;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Settings;

namespace BotCH.Core.Brain;

/// <summary>
/// Возврат в центр фарма: перс за радиусом фарма (погнался за мобом), а делать нечего (бой ищет цель и не нашёл, копать нечего)
/// не меньше <see cref="IdleBefore"/> — бежим в центр и ждём мобов там. Внутри радиуса стоим, где стоим; радиус 0 — не возвращаемся.
/// Бег фоновый: любое действие боя его перебивает.
/// </summary>
public sealed class ReturnBehavior(CombatBehavior combat) : IBehavior
{
    // Дальше — центр, видимо, в другом месте (сохранён в другой локации): не бежим через полкарты
    private const float MaxDistance = 300f;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Сколько подряд должно быть нечего делать. После убийства и лута бой на шаг отдаёт ход, а следующего моба выбирает
    /// шагом позже — без паузы бег в центр успевал начаться (и игра — строить путь) и тут же отменялся подходом к мобу.
    /// </summary>
    public static readonly TimeSpan IdleBefore = TimeSpan.FromSeconds(2);
    // Шаги мозга — раз в ~0.25 с; пропуск дольше — ход забирал кто-то другой, «нечего делать» начинается заново
    private static readonly TimeSpan TickGap = TimeSpan.FromSeconds(0.6);

    private DateTime _nextTry = DateTime.MinValue;
    private DateTime _idleSince = DateTime.MinValue;
    private DateTime _lastIdle = DateTime.MinValue;

    public string Name => "возврат";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!c.Settings.Target.ReturnToCenter || c.FarmCenter is not { } center || !c.Runner.Actions.CanMove)
            return false;
        if (combat.State != CombatState.Search)
            return false;

        var w = c.World;
        if (c.Runner.Pending.OfType<MoveAction>().Any(m => m.Priority == ActionPriority.Background))
        {
            Status = "возвращаюсь в центр фарма";
            return true;
        }

        var distance = w.Host.Position.HorizontalDistanceTo(center);
        if (c.Settings.Target.FarmRadius <= 0 || c.InFarmArea(w.Host.Position) || c.Now < _nextTry || c.Runner.BodyBusy(w) is not null)
            return false;

        if (c.Now - _lastIdle > TickGap)
            _idleSince = c.Now;
        _lastIdle = c.Now;
        if (c.Now - _idleSince < IdleBefore)
            return false;
        if (distance > MaxDistance)
        {
            c.Say("return-too-far", $"Центр фарма в {distance:0} м — слишком далеко, не возвращаюсь", LogLevel.Warning, 300);
            return false;
        }

        var smart = c.Settings.Target.ReturnPath == ApproachPath.Smart;
        var sent = c.Send(new MoveAction(center, tolerance: 2f, smart) { Priority = ActionPriority.Background });
        if (sent == SubmitStatus.Sent)
            c.Log.Info($"Вне радиуса фарма, целей нет — возвращаюсь в центр, {distance:0} м");
        Status = "возвращаюсь в центр фарма";
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        // Не дошли — не долбим заново каждый шаг
        if (outcome.Action is MoveAction { Priority: ActionPriority.Background } && outcome.Status is ActionStatus.Rejected or ActionStatus.Timeout or ActionStatus.Failed)
            _nextTry = c.Now + RetryAfter;
    }

    public void Reset()
    {
        _nextTry = DateTime.MinValue;
        _idleSince = _lastIdle = DateTime.MinValue;
        Status = null;
    }
}

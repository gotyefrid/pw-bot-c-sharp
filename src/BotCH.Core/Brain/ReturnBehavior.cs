using System;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Settings;

namespace BotCH.Core.Brain;

/// <summary>
/// Возврат в центр фарма: делать нечего (бой ищет цель и не нашёл, копать нечего), а перс дальше <see cref="Arrived"/> м
/// от центра — бежим туда и ждём мобов там. Бег фоновый: любое действие боя его перебивает.
/// </summary>
public sealed class ReturnBehavior(CombatBehavior combat) : IBehavior
{
    /// <summary>Ближе — уже в центре.</summary>
    public const float Arrived = 5f;
    // Дальше — центр, видимо, в другом месте (сохранён в другой локации): не бежим через полкарты
    private const float MaxDistance = 300f;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private DateTime _nextTry = DateTime.MinValue;

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
        if (distance <= Arrived || c.Now < _nextTry || c.Runner.BodyBusy(w) is not null)
            return false;
        if (distance > MaxDistance)
        {
            c.Say("return-too-far", $"Центр фарма в {distance:0} м — слишком далеко, не возвращаюсь", LogLevel.Warning, 300);
            return false;
        }

        var smart = c.Settings.Target.ReturnPath == ApproachPath.Smart;
        var sent = c.Send(new MoveAction(center, tolerance: 2f, smart) { Priority = ActionPriority.Background });
        if (sent == SubmitStatus.Sent)
            c.Log.Info($"Целей нет — возвращаюсь в центр фарма, {distance:0} м");
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
        Status = null;
    }
}

using System;
using Notes.Core.Actions;
using Notes.Core.Brain;

namespace Notes.Core.Clicker;

public enum StepStatus
{
    /// <summary>Ещё идёт — следующий шаг ждёт.</summary>
    Running,
    /// <summary>Сделано — к следующему шагу.</summary>
    Done,
    /// <summary>Не вышло — в лог, и к следующему шагу.</summary>
    Failed,
}

public readonly record struct StepState(StepStatus Status, string Details = "")
{
    public static readonly StepState Running = new(StepStatus.Running);
    public static readonly StepState Done = new(StepStatus.Done);
    public static StepState Failed(string why) => new(StepStatus.Failed, why);
}

/// <summary>
/// Выполнение действия кликера — своё на каждый запуск шага (<see cref="ClickerAction.Start"/>): тут его состояние (когда
/// началась пауза, что отправлено в игру), а в действии — только настройки.
/// </summary>
public interface IClickerRun
{
    /// <summary>Сделать ход: первый — сразу при запуске шага, дальше — на каждом снимке, пока <see cref="StepStatus.Running"/>.</summary>
    StepState Tick(BrainContext c);

    /// <summary>Итог действия игры, отправленного кликером (может быть и не этим выполнением — своё узнавать по ссылке).</summary>
    void OnOutcome(BrainContext c, ActionOutcome outcome);
}

/// <summary>Готовые выполнения — из них собираются действия кликера.</summary>
public static class ClickerRuns
{
    /// <summary>Отправить действие игры и дождаться итога по снимкам: подтвердилось — сделано, иначе — не вышло.</summary>
    /// <param name="doneWhenSent">Сделано, как только ушло (клавиша: проверять нечего; подбор без галочки «ждать») — следующий шаг в тот же ход.</param>
    public static IClickerRun Send(GameAction action, bool doneWhenSent = false) => new SendRun(action, doneWhenSent);

    /// <summary>Подождать: сделано, когда с первого хода прошло <paramref name="time"/> (по снимкам — с шагом 0,25 с).</summary>
    public static IClickerRun Wait(TimeSpan time) => new WaitRun(time);

    /// <summary>Сразу «не вышло» — делать нечего (рядом нет моба, клавиша не выбрана).</summary>
    public static IClickerRun Failed(string why) => new FixedRun(StepState.Failed(why));

    /// <summary>Сразу «сделано» — делать нечего, и это не ошибка (поднимать нечего): почему — в подробный лог.</summary>
    public static IClickerRun Skipped(string why) => new FixedRun(new StepState(StepStatus.Done, why));

    private sealed class SendRun(GameAction action, bool doneWhenSent) : IClickerRun
    {
        private bool _sent;
        private ActionOutcome? _outcome;

        public StepState Tick(BrainContext c)
        {
            if (!_sent)
            {
                switch (c.Send(action))
                {
                    case SubmitStatus.Sent:
                        _sent = true;
                        return doneWhenSent ? StepState.Done : StepState.Running;
                    // Тело занято чужим — попробовать на следующем снимке
                    case SubmitStatus.Busy:
                        return StepState.Running;
                    // Такое же уже ждёт итога — дело и так делается
                    case SubmitStatus.AlreadyPending:
                        return StepState.Done;
                    default:
                        return StepState.Failed("не отправлено (причина — в логе выше)");
                }
            }

            if (_outcome is null)
                return StepState.Running;
            return _outcome.Status == ActionStatus.Confirmed ? StepState.Done : StepState.Failed(_outcome.ToString());
        }

        public void OnOutcome(BrainContext c, ActionOutcome outcome)
        {
            if (ReferenceEquals(outcome.Action, action))
                _outcome = outcome;
        }
    }

    private sealed class WaitRun(TimeSpan time) : IClickerRun
    {
        private DateTime? _start;

        public StepState Tick(BrainContext c)
        {
            _start ??= c.Now;
            return c.Now - _start.Value >= time ? StepState.Done : StepState.Running;
        }

        public void OnOutcome(BrainContext c, ActionOutcome outcome)
        {
        }
    }

    private sealed class FixedRun(StepState state) : IClickerRun
    {
        public StepState Tick(BrainContext c) => state;

        public void OnOutcome(BrainContext c, ActionOutcome outcome)
        {
        }
    }
}

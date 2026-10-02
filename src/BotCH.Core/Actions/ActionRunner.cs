using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.Core.Actions;

public enum ActionStatus
{
    /// <summary>Снимки подтвердили, что сработало.</summary>
    Confirmed,
    /// <summary>Игра не приняла (стопка не изменилась, цель сменилась…).</summary>
    Rejected,
    /// <summary>Не дождались подтверждения.</summary>
    Timeout,
    /// <summary>Не отправлено: условие не выполнено или вызов отказал.</summary>
    Failed,
    /// <summary>Больше не нужно (цель умерла раньше) — не ошибка.</summary>
    Cancelled,
}

public sealed record ActionOutcome(GameAction Action, ActionStatus Status, string Details, TimeSpan Elapsed)
{
    public override string ToString()
    {
        var mark = Status switch
        {
            ActionStatus.Confirmed => "✓",
            ActionStatus.Rejected => "✗ отказ",
            ActionStatus.Timeout => "✗ не подтвердилось",
            ActionStatus.Cancelled => "отменено",
            _ => "✗ не отправлено",
        };
        return $"{Action.Name}: {mark}{(Details.Length > 0 ? " — " + Details : "")}"
               + (Status == ActionStatus.Failed ? "" : $" ({Elapsed.TotalSeconds:0.0} с)");
    }
}

public enum SubmitStatus
{
    Sent,
    /// <summary>Такое же действие ещё ждёт подтверждения — второй раз не отправлено.</summary>
    AlreadyPending,
    /// <summary>Тело занято другим действием — не отправлено, это не ошибка: отправить позже.</summary>
    Busy,
    Failed,
}

/// <param name="BusyWith">При <see cref="SubmitStatus.Busy"/> — чем занято тело.</param>
public sealed record SubmitResult(SubmitStatus Status, ActionOutcome? Outcome = null, GameAction? BusyWith = null)
{
    public bool Sent => Status == SubmitStatus.Sent;
}

/// <summary>
/// Единственный исполнитель действий. Вызовы в игре идут строго по одному (lock), действие «в процессе» не
/// отправляется повторно, подтверждение — по снимкам (<see cref="Update"/>), время — по меткам снимков.
/// Тело персонажа (<see cref="ActionResource.Body"/>) занимает одно действие за раз: второе получает «занято».
/// </summary>
public sealed class ActionRunner(IGameActions actions, ILogger log)
{
    private readonly object _lock = new();
    private readonly List<(GameAction Action, WorldState Start)> _pending = [];

    public IGameActions Actions { get; } = actions;

    /// <summary>Результат каждого действия (и неотправленного тоже).</summary>
    public event Action<ActionOutcome>? Completed;

    public bool IsPending(string key)
    {
        lock (_lock)
            return _pending.Any(p => p.Action.Key == key);
    }

    /// <summary>Действие, которое сейчас занимает тело; null — свободно.</summary>
    public GameAction? BodyAction
    {
        get
        {
            lock (_lock)
                return _pending.Select(p => p.Action).FirstOrDefault(a => a.Resource == ActionResource.Body);
        }
    }

    public IReadOnlyList<GameAction> Pending
    {
        get
        {
            lock (_lock)
                return _pending.Select(p => p.Action).ToList();
        }
    }

    /// <summary>
    /// Отправить вместо такого же ожидающего (тот же <see cref="GameAction.Key"/>) — например, новая точка, пока бежим к старой.
    /// Прежнее просто забывается: игра сама заменяет текущую работу новой.
    /// </summary>
    public SubmitResult Replace(GameAction action, WorldState now)
    {
        lock (_lock)
        {
            if (_pending.RemoveAll(p => p.Action.Key == action.Key) > 0)
                log.Debug($"↺ {action.Name}");
            return Submit(action, now);
        }
    }

    public SubmitResult Submit(GameAction action, WorldState now)
    {
        ActionOutcome outcome;
        lock (_lock)
        {
            if (_pending.Any(p => p.Action.Key == action.Key))
                return new SubmitResult(SubmitStatus.AlreadyPending);

            if (action.Resource == ActionResource.Body && BodyAction is { } busy)
                return new SubmitResult(SubmitStatus.Busy, BusyWith: busy);

            if (action.Precondition(now) is { } reason)
            {
                outcome = new ActionOutcome(action, ActionStatus.Failed, reason, TimeSpan.Zero);
            }
            else
            {
                var call = action.Send(Actions, now);
                if (call.Ok)
                {
                    _pending.Add((action, now));
                    log.Debug($"→ {action.Name}");
                    return new SubmitResult(SubmitStatus.Sent);
                }

                outcome = new ActionOutcome(action, ActionStatus.Failed, call.Details, TimeSpan.Zero);
            }
        }

        Report(outcome);
        return new SubmitResult(SubmitStatus.Failed, outcome);
    }

    /// <summary>Проверяет ждущие действия по новому снимку. Возвращает завершившиеся.</summary>
    public IReadOnlyList<ActionOutcome> Update(WorldState now)
    {
        var done = new List<ActionOutcome>();
        lock (_lock)
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var (action, start) = _pending[i];
                var elapsed = now.Time - start.Time;
                var verdict = action.Check(start, now);
                ActionOutcome? outcome = verdict.Kind switch
                {
                    VerdictKind.Confirmed => new(action, ActionStatus.Confirmed, verdict.Details, elapsed),
                    VerdictKind.Rejected => new(action, ActionStatus.Rejected, verdict.Details, elapsed),
                    VerdictKind.Cancelled => new(action, ActionStatus.Cancelled, verdict.Details, elapsed),
                    _ when elapsed >= action.Timeout => new(action, action.TimeoutStatus,
                        action.TimeoutStatus == ActionStatus.Rejected ? "игра не приняла" : $"нет подтверждения за {action.Timeout.TotalSeconds:0} с", elapsed),
                    _ => null,
                };

                if (outcome is null)
                    continue;

                _pending.RemoveAt(i);
                done.Add(outcome);
            }
        }

        done.Reverse();
        foreach (var outcome in done)
            Report(outcome);
        return done;
    }

    /// <summary>Забыть всё ожидающее (бот остановлен, сменился клиент).</summary>
    public void Clear()
    {
        lock (_lock)
            _pending.Clear();
    }

    private void Report(ActionOutcome outcome)
    {
        log.Log(outcome.Status is ActionStatus.Confirmed or ActionStatus.Cancelled ? LogLevel.Info : LogLevel.Warning, outcome.ToString());
        Completed?.Invoke(outcome);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
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

/// <param name="Busy">При <see cref="SubmitStatus.Busy"/> — чем занято тело («бег …», «персонаж кастует»).</param>
public sealed record SubmitResult(SubmitStatus Status, ActionOutcome? Outcome = null, string? Busy = null)
{
    public bool Sent => Status == SubmitStatus.Sent;
}

/// <summary>
/// Единственный исполнитель действий. Вызовы в игре идут строго по одному (lock), подтверждение — по снимкам
/// (<see cref="Update"/>), время — по меткам снимков. У каждого действия есть хозяин (<see cref="IActionOwner"/>) — кто его
/// отправил; итог (и «не отправлено», и «отменено») уходит в <see cref="Completed"/> вместе с ним.
/// Кто кого перебивает, решается здесь, по <see cref="GameAction.Priority"/>, два правила подряд:
/// <list type="bullet">
/// <item>слот (<see cref="GameAction.Key"/>): в нём ждёт своё же действие или в игре уже идёт то же самое
/// (<see cref="GameAction.SameInGame"/>) — «уже ждёт», чужое — см. ниже;</item>
/// <item>тело (<see cref="ActionResource.Body"/>) занимает одно действие за раз; пока персонаж кастует или копает — тоже занято.</item>
/// </list>
/// Чужое ждущее (в слоте или в теле) ниже по важности забывается — итог «отменено» его хозяину: игра сама заменит его работу
/// новой; такое же или важнее — «занято». Копание в игре прерывается отменой (как Esc) ради обычного и срочного, чужой
/// каст — только ради срочного и только если известно, какой скилл кастуется (свой же каст не сбиваем).
/// </summary>
public sealed class ActionRunner(IGameActions actions, ILogger log) : IPendingActions
{
    private readonly object _lock = new();
    private readonly List<(GameAction Action, WorldState Start)> _pending = [];

    public IGameActions Actions { get; } = actions;

    public Capabilities Capabilities => Actions.Capabilities;

    /// <summary>Результат каждого действия (и неотправленного, и отменённого); хозяин — в <see cref="GameAction.Owner"/>.</summary>
    public event Action<ActionOutcome>? Completed;

    public bool IsPending(ActionKey key)
    {
        lock (_lock)
            return _pending.Any(p => p.Action.Key == key);
    }

    public bool IsPending(ActionSlot slot)
    {
        lock (_lock)
            return _pending.Any(p => p.Action.Slot == slot);
    }

    public IReadOnlyList<GameAction> PendingOf(IActionOwner owner)
    {
        lock (_lock)
            return _pending.Select(p => p.Action).Where(a => a.Owner == owner).ToList();
    }

    public GameAction? BodyAction
    {
        get
        {
            lock (_lock)
                return _pending.Select(p => p.Action).FirstOrDefault(a => a.Resource == ActionResource.Body);
        }
    }

    public string? BodyBusy(WorldState now)
        => BodyAction is { } action ? action.Name
            : now.Host.IsCasting ? "персонаж кастует"
            : now.Host.Gather is { Active: true } ? "персонаж копает"
            : null;

    public IReadOnlyList<GameAction> Pending
    {
        get
        {
            lock (_lock)
                return _pending.Select(p => p.Action).ToList();
        }
    }

    /// <summary>
    /// Отправить вместо своего такого же ожидающего (тот же <see cref="GameAction.Key"/>) — например, новая точка, пока бежим
    /// к старой. Прежнее просто забывается: игра сама заменяет текущую работу новой. Чужое ждущее в слоте — как при
    /// <see cref="Submit"/>: уступит, только если новое важнее.
    /// </summary>
    public SubmitResult Replace(IActionOwner owner, GameAction action, WorldState now)
    {
        lock (_lock)
        {
            if (_pending.RemoveAll(p => p.Action.Key == action.Key && p.Action.Owner == owner) > 0)
                log.Debug($"↺ {action.Name}");
            return Submit(owner, action, now);
        }
    }

    public SubmitResult Submit(IActionOwner owner, GameAction action, WorldState now)
    {
        var reports = new List<ActionOutcome>();
        SubmitResult result;
        action.Owner = owner;
        lock (_lock)
            result = SubmitLocked(action, now, reports);

        foreach (var outcome in reports)
            Report(outcome);
        return result;
    }

    private SubmitResult SubmitLocked(GameAction action, WorldState now, List<ActionOutcome> reports)
    {
        var inSlot = _pending.FindIndex(p => p.Action.Key == action.Key);
        if (inSlot >= 0)
        {
            var holder = _pending[inSlot].Action;
            if (holder.Owner == action.Owner || action.SameInGame(holder))
                return new SubmitResult(SubmitStatus.AlreadyPending);
            if (Displace(inSlot, action, now, reports) is { } slotBusy)
                return new SubmitResult(SubmitStatus.Busy, Busy: slotBusy);
        }

        if (action.Resource == ActionResource.Body && FreeBody(action, now, reports) is { } busy)
            return new SubmitResult(SubmitStatus.Busy, Busy: busy);

        ActionOutcome outcome;
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

        reports.Add(outcome);
        return new SubmitResult(SubmitStatus.Failed, outcome);
    }

    /// <summary>
    /// Освободить тело для <paramref name="action"/> по важности. null — свободно, можно слать; иначе чем занято
    /// (в том числе «прерываю …» — отмена каста или копания ушла, тело освободится через миг).
    /// </summary>
    private string? FreeBody(GameAction action, WorldState now, List<ActionOutcome> reports)
    {
        var index = _pending.FindIndex(p => p.Action.Resource == ActionResource.Body);
        if (index >= 0 && Displace(index, action, now, reports) is { } holder)
            return holder;

        var digging = now.Host.Gather is { Active: true };
        if (!now.Host.IsCasting && !digging)
            return null;

        // Хозяин отмены — тот, ради чьего действия прерываем
        var cancel = new CancelAction(digging ? "копание" : "каст") { Owner = action.Owner };
        if (_pending.Any(p => p.Action.Slot == ActionSlot.Cancel))
            return $"прерываю {cancel.What}";

        // Свой же каст не сбиваем; чужой — только срочным и только зная, что кастуется (вдруг это и есть наше лечение)
        var casting = now.Host.CastingSkillId ?? 0;
        if (!digging && action.CastsSkill != 0 && casting == action.CastsSkill)
            return "кастуется этот же скилл";
        var canBreak = digging ? action.Priority > ActionPriority.Background : action.Priority == ActionPriority.Urgent && casting != 0;
        if (!canBreak || !Actions.Capabilities.Has(Capability.Cancel))
            return digging ? "персонаж копает" : "персонаж кастует";

        var call = cancel.Send(Actions, now);
        if (!call.Ok)
        {
            reports.Add(new ActionOutcome(cancel, ActionStatus.Failed, call.Details, TimeSpan.Zero));
            return digging ? "персонаж копает" : "персонаж кастует";
        }

        _pending.Add((cancel, now));
        log.Info($"Прерываю {cancel.What} ради «{action.Name}»");
        return $"прерываю {cancel.What}";
    }

    /// <summary>
    /// Ждущее (в слоте или в теле) такое же по важности или важнее нового — его имя: «занято». Иначе оно забывается — итог
    /// «отменено» его хозяину — и null.
    /// </summary>
    private string? Displace(int index, GameAction action, WorldState now, List<ActionOutcome> reports)
    {
        var (holder, start) = _pending[index];
        if (holder.Priority >= action.Priority)
            return holder.Name;

        _pending.RemoveAt(index);
        reports.Add(new ActionOutcome(holder, ActionStatus.Cancelled, $"важнее: {action.Name}", now.Time - start.Time));
        return null;
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

    /// <summary>
    /// Больше не ждать этого действия: стало не нужно (моб отстал, пока поднимались). В игре его работу заменит следующее действие.
    /// </summary>
    public void Forget(GameAction action)
    {
        lock (_lock)
        {
            if (_pending.RemoveAll(p => p.Action == action) > 0)
                log.Debug($"✕ {action.Name}");
        }
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

using System;
using System.Collections.Generic;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>Всё, что нужно поведению на одном шаге мозга.</summary>
public sealed class BrainContext
{
    private readonly Dictionary<string, DateTime> _lastSaid = [];

    // Отказы при отправке («не отправлено») — исполнитель отдаёт их сразу, посреди хода поведения. Раздать поведениям
    // можно только после их хода (BotBrain), иначе OnOutcome сработает посреди чужого (или своего же) Tick
    private readonly List<ActionOutcome> _notSent = [];

    internal BrainContext(ActionRunner runner, ClassSkills skills, ILogger log, Random random)
    {
        Runner = runner;
        Skills = skills;
        Log = log;
        Random = random;
        // Ловим на исполнителе, а не в Submit: отправляют и в обход (Runner.Replace) — отказ не должен теряться нигде
        runner.Completed += outcome =>
        {
            if (outcome.Status == ActionStatus.Failed)
                _notSent.Add(outcome);
        };
    }

    /// <summary>Отказы при отправке за этот ход — мозг раздаёт их поведениям после их хода. Очередь очищается.</summary>
    internal IReadOnlyList<ActionOutcome> TakeNotSent()
    {
        if (_notSent.Count == 0)
            return [];

        var taken = _notSent.ToArray();
        _notSent.Clear();
        return taken;
    }

    public ActionRunner Runner { get; }
    public ClassSkills Skills { get; }
    public ILogger Log { get; }
    public Random Random { get; }

    /// <summary>Копия настроек для мозга (окно правит свою, сюда попадает новая копия между шагами).</summary>
    public BotSettings Settings { get; internal set; } = new();

    public WorldState World { get; internal set; } = null!;

    /// <summary>Где стоял персонаж, когда бот запустили.</summary>
    public Position? StartPosition { get; internal set; }

    /// <summary>Центр фарма: выбранная сохранённая точка, иначе точка старта. От него считается радиус фарма.</summary>
    public Position? FarmCenter => Settings.Target.SelectedFarmPoint?.Position ?? StartPosition;

    /// <summary>Моб в радиусе фарма от центра (или радиус не задан).</summary>
    public bool InFarmArea(NpcInfo mob) => InFarmArea(mob.Position);

    /// <summary>Точка в радиусе фарма от центра (или радиус не задан).</summary>
    public bool InFarmArea(Position point)
    {
        var radius = Settings.Target.FarmRadius;
        return radius <= 0 || FarmCenter is not { } center || point.HorizontalDistanceTo(center) <= radius;
    }

    public DateTime Now => World.Time;

    /// <summary>Поведение просит остановить бота (маршрут пуст, нет кирки…); мозг остановит после этого шага.</summary>
    public string? StopReason { get; internal set; }

    /// <summary>Остановить бота с причиной в логе. true — ход занят (дальше в этом шаге ничего не делаем).</summary>
    public bool RequestStop(string reason)
    {
        StopReason ??= reason;
        return true;
    }

    /// <summary>
    /// Отправить действие. true — отправлено или такое уже ждёт подтверждения (ход занят ожиданием).
    /// Тело занято другим действием — false: ход не занят, пусть решают другие поведения.
    /// </summary>
    public bool Submit(GameAction action) => Send(action) is SubmitStatus.Sent or SubmitStatus.AlreadyPending;

    /// <summary>Отправить и узнать, что вышло: Sent — ушло сейчас (можно писать в лог «лечу», сдвигать таймеры).</summary>
    public SubmitStatus Send(GameAction action)
    {
        var result = Runner.Submit(action, World);
        if (result.Status == SubmitStatus.Busy)
            Log.Debug($"{action.Name}: тело занято — {result.Busy}");
        return result.Status;
    }

    /// <summary>
    /// Сообщение в лог не чаще раза в <paramref name="seconds"/> с для одного ключа —
    /// «нет банок» не должно заливать лог 4 раза в секунду.
    /// </summary>
    public void Say(string key, string message, LogLevel level = LogLevel.Info, double seconds = 30)
    {
        if (_lastSaid.TryGetValue(key, out var last) && Now - last < TimeSpan.FromSeconds(seconds))
            return;

        _lastSaid[key] = Now;
        Log.Log(level, message);
    }
}

/// <summary>Одно поведение мозга (банки, пет, бой). Маленький класс с одной задачей.</summary>
public interface IBehavior
{
    string Name { get; }

    /// <summary>Сделать шаг. true — ход занят (отправлено действие или идёт важное ожидание), дальше по приоритету не идём.</summary>
    bool Tick(BrainContext context);

    /// <summary>Итог отправленного действия (приходит всем поведениям).</summary>
    void OnOutcome(BrainContext context, ActionOutcome outcome);

    /// <summary>Забыть состояние (стоп/старт).</summary>
    void Reset();

    /// <summary>Что делает сейчас — для окна. null — ничего.</summary>
    string? Status { get; }
}

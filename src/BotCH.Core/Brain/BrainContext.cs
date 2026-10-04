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

    // Итоги действий: исполнитель отдаёт их по ходу — на снимке, а «не отправлено» и «отменено» — прямо при отправке,
    // посреди хода поведения. Раздать хозяевам можно только между ходами (BotBrain), иначе OnOutcome сработает посреди
    // чужого (или своего же) Tick
    private readonly List<ActionOutcome> _outcomes = [];

    internal BrainContext(ActionRunner runner, ClassSkills skills, ILogger log, Random random)
    {
        Executor = runner;
        Skills = skills;
        Log = log;
        Random = random;
        runner.Completed += _outcomes.Add;
    }

    /// <summary>Итоги действий с прошлого раза — мозг раздаёт их хозяевам. Очередь очищается.</summary>
    internal IReadOnlyList<ActionOutcome> TakeOutcomes()
    {
        if (_outcomes.Count == 0)
            return [];

        var taken = _outcomes.ToArray();
        _outcomes.Clear();
        return taken;
    }

    /// <summary>Исполнитель: проверка по снимку и сброс — у мозга, отправка — через <see cref="Send"/> (с хозяином).</summary>
    internal ActionRunner Executor { get; }

    /// <summary>Что ждёт подтверждения и чем занято тело — только посмотреть; отправлять через <see cref="Send"/>.</summary>
    public IPendingActions Runner => Executor;

    /// <summary>Чьё поведение сейчас ходит или получает итог — хозяин отправляемых действий. Ставит мозг.</summary>
    internal IActionOwner? Owner { get; set; }

    private IActionOwner Me => Owner ?? throw new InvalidOperationException("Действие отправлено вне хода поведения — у него не будет хозяина");

    /// <summary>Свои ждущие действия (этого поведения).</summary>
    public IReadOnlyList<GameAction> Mine => Executor.PendingOf(Me);

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

    /// <summary>
    /// Отправить и узнать, что вышло: Sent — ушло сейчас (можно писать в лог «лечу», сдвигать таймеры). Хозяин — поведение,
    /// которое сейчас ходит: итог придёт ему в <see cref="IBehavior.OnOutcome"/>.
    /// </summary>
    public SubmitStatus Send(GameAction action) => Said(action, Executor.Submit(Me, action, World));

    /// <summary>Отправить вместо своего такого же ждущего (новая точка, пока бежим к старой); чужое — уступит, только если это важнее.</summary>
    public SubmitStatus Replace(GameAction action) => Said(action, Executor.Replace(Me, action, World));

    /// <summary>Больше не ждать своего действия: стало не нужно (моб отстал, пока поднимались).</summary>
    public void Forget(GameAction action) => Executor.Forget(action);

    private SubmitStatus Said(GameAction action, SubmitResult result)
    {
        if (result.Status == SubmitStatus.Busy)
            Log.Debug($"{action.Name}: занято — {result.Busy}");
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

/// <summary>Одно поведение мозга (банки, пет, бой). Маленький класс с одной задачей. Хозяин своих действий.</summary>
public interface IBehavior : IActionOwner
{
    /// <summary>Сделать шаг. true — ход занят (отправлено действие или идёт важное ожидание), дальше по приоритету не идём.</summary>
    bool Tick(BrainContext context);

    /// <summary>
    /// Итог своего действия: подтверждено, отказ, не дождались, не отправлено или отменено (вытеснило более важное чужое).
    /// Приходит между ходами, не посреди <see cref="Tick"/>.
    /// </summary>
    void OnOutcome(BrainContext context, ActionOutcome outcome);

    /// <summary>Забыть состояние (стоп/старт).</summary>
    void Reset();

    /// <summary>Что делает сейчас — для окна. null — ничего.</summary>
    string? Status { get; }
}

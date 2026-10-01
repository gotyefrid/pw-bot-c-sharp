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

    internal BrainContext(ActionRunner runner, ClassSkills skills, ILogger log, Random random)
    {
        Runner = runner;
        Skills = skills;
        Log = log;
        Random = random;
    }

    public ActionRunner Runner { get; }
    public ClassSkills Skills { get; }
    public ILogger Log { get; }
    public Random Random { get; }

    /// <summary>Копия настроек для мозга (окно правит свою, сюда попадает новая копия между шагами).</summary>
    public BotSettings Settings { get; internal set; } = new();

    public WorldState World { get; internal set; } = null!;

    public DateTime Now => World.Time;

    /// <summary>Отправить действие. true — отправлено или такое уже ждёт подтверждения (ход занят ожиданием).</summary>
    public bool Submit(GameAction action)
    {
        var result = Runner.Submit(action, World);
        return result.Status != SubmitStatus.Failed;
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

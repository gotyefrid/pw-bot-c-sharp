using System;
using System.Collections.Generic;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Режим работы бота — свой ход выполнения на каждом снимке. Окно знает только этот интерфейс:
/// новый режим — новый класс, остальные не трогаются.
/// </summary>
public interface IBotRunner
{
    /// <summary>Что делает бот — для окна.</summary>
    string Status { get; }

    event Action<string>? StatusChanged;

    /// <summary>Режим сам просит остановку (персонаж погиб и т.п.).</summary>
    event Action<string>? StopRequested;

    void Tick(WorldState world);

    void UpdateSettings(BotSettings settings);

    void Reset();
}

public static class BotModes
{
    public static string Title(BotMode mode) => mode switch
    {
        BotMode.GatherResources => "Собирать ресурсы",
        BotMode.Clicker => "Кликер",
        _ => "Бить мобов",
    };

    /// <param name="routeStart">Обход: с какой точки начинать (с 0) — выбранная в окне.</param>
    public static IBotRunner Create(BotMode mode, ActionRunner runner, ClassSkills skills, BotSettings settings, ILogger log,
        IReadOnlyCollection<uint>? gatherTools = null, int routeStart = 0)
        => mode switch
        {
            BotMode.GatherResources => new BotBrain(runner, skills, settings, log, gatherTools: gatherTools, mode: BotMode.GatherResources, routeStart: routeStart),
            BotMode.Clicker => new NotReadyMode("кликер", "часть 10", log),
            _ => new BotBrain(runner, skills, settings, log, gatherTools: gatherTools),
        };
}

/// <summary>
/// Заготовка режима, который ещё не сделан: ничего не делает в игре, только честно об этом пишет.
/// Место, куда встанет свой ход выполнения (маршрут сбора, шаги кликера).
/// </summary>
public sealed class NotReadyMode(string name, string where, ILogger log) : IBotRunner
{
    private bool _said;

    public string Status { get; } = $"режим «{name}» ещё не сделан ({where})";

    public event Action<string>? StatusChanged;

    // Заготовка сама не останавливается
    public event Action<string>? StopRequested
    {
        add { }
        remove { }
    }

    public void Tick(WorldState world)
    {
        if (_said)
            return;

        _said = true;
        log.Warning($"Режим «{name}» ещё не сделан ({where}) — бот ничего не делает");
        StatusChanged?.Invoke(Status);
    }

    public void UpdateSettings(BotSettings settings)
    {
    }

    public void Reset() => _said = false;
}

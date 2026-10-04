using System;
using System.Collections.Generic;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Режим работы бота — свой ход выполнения на каждом снимке. Окно знает только этот интерфейс: новый режим — новый набор
/// поведений в <see cref="BotModes"/> (или свой класс), мозг и окно не трогаются.
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

    /// <summary>
    /// Часть режима с нужной окну возможностью: <see cref="IRouteProgress"/> — какая точка маршрута текущая (потом так же —
    /// текущий шаг кликера). Окно спрашивает возможность, а не класс режима. Нет такой части — null.
    /// </summary>
    T? Part<T>() where T : class;
}

public static class BotModes
{
    public static string Title(BotMode mode) => mode switch
    {
        BotMode.GatherResources => "Собирать ресурсы",
        BotMode.Clicker => "Кликер",
        _ => "Бить мобов",
    };

    /// <summary>
    /// Собрать режим: мозг (<see cref="BotBrain"/>) один, режимы отличаются набором поведений и их порядком. Кликер
    /// (часть 10) — ещё один набор: [банки?, пет?, шаги].
    /// </summary>
    /// <param name="routeStart">Обход: с какой точки начинать (с 0) — выбранная в окне.</param>
    /// <param name="random">Паузы и разброс; тесты задают свой, чтобы ход повторялся.</param>
    public static IBotRunner Create(BotMode mode, ActionRunner runner, ClassSkills skills, BotSettings settings, ILogger log,
        IReadOnlyCollection<uint>? gatherTools = null, int routeStart = 0, Random? random = null)
    {
        if (mode == BotMode.Clicker)
            return new NotReadyMode("кликер", "часть 10", log);

        // Копия настроек: окно может менять свои дальше (новые мозг получит через UpdateSettings)
        var context = new BrainContext(runner, skills, log, random ?? new Random()) { Settings = settings.Clone() };
        var tools = gatherTools ?? [];
        return mode == BotMode.GatherResources ? GatherResources(context, tools, routeStart) : FarmMobs(context, tools);
    }

    /// <summary>
    /// Фарм мобов: выжить → пет → копать ресурсы в радиусе фарма → бой → лут → вернуться в центр фарма. Первым — запись центра
    /// фарма в лог (хода не занимает).
    /// </summary>
    private static BotBrain FarmMobs(BrainContext context, IReadOnlyCollection<uint> tools)
    {
        var combat = new CombatBehavior();
        return new BotBrain(context,
            [new FarmCenterLog(), new SurvivalBehavior(), new PetBehavior(), new GatherBehavior(tools), combat, new LootBehavior(), new ReturnBehavior()],
            main: combat);
    }

    /// <summary>
    /// Обход ресурсов: выжить → уйти вверх от опасного моба → пет → копать у текущей точки → защита (бьём только напавших) →
    /// лут → к следующей точке.
    /// </summary>
    private static BotBrain GatherResources(BrainContext context, IReadOnlyCollection<uint> tools, int routeStart)
    {
        var combat = new CombatBehavior(defendOnly: true);
        var route = new RouteBehavior(tools, routeStart);
        return new BotBrain(context,
            [new SurvivalBehavior(), new EscapeBehavior(route), new PetBehavior(), new GatherBehavior(tools, route), combat, new LootBehavior(), route],
            main: combat);
    }
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

    public T? Part<T>() where T : class => null;
}

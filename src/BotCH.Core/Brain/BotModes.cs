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

    /// <summary>Собрать режим: мозг (<see cref="BotBrain"/>) один, режимы отличаются набором поведений и их порядком.</summary>
    /// <param name="routeStart">Обход: с какой точки начинать (с 0) — выбранная в окне.</param>
    /// <param name="random">Паузы и разброс; тесты задают свой, чтобы ход повторялся.</param>
    public static IBotRunner Create(BotMode mode, ActionRunner runner, ClassSkills skills, BotSettings settings, ILogger log,
        IReadOnlyCollection<uint>? gatherTools = null, int routeStart = 0, Random? random = null)
    {
        // Копия настроек: окно может менять свои дальше (новые мозг получит через UpdateSettings)
        var context = new BrainContext(runner, skills, log, random ?? new Random()) { Settings = settings.Clone() };
        var tools = gatherTools ?? [];
        return mode switch
        {
            BotMode.GatherResources => GatherResources(context, tools, routeStart),
            BotMode.Clicker => Clicker(context),
            _ => FarmMobs(context, tools),
        };
    }

    /// <summary>Кликер: только правила из списка — без банок, пета и боя (банка — правилом «HP меньше %»).</summary>
    private static BotBrain Clicker(BrainContext context)
    {
        var clicker = new ClickerBehavior();
        return new BotBrain(context, [clicker], main: clicker);
    }

    /// <summary>
    /// Фарм мобов: выжить → (прервать каст без цели) → пет → копать ресурсы в радиусе фарма → бой → лут → вернуться в центр фарма.
    /// Первым — запись центра фарма в лог; ни она, ни прерывание каста хода не занимают.
    /// </summary>
    private static BotBrain FarmMobs(BrainContext context, IReadOnlyCollection<uint> tools)
    {
        var combat = new CombatBehavior();
        return new BotBrain(context,
            [new FarmCenterLog(), new SurvivalBehavior(), new UselessCastBehavior(), new PetBehavior(), new GatherBehavior(tools), combat,
             new LootBehavior(), new ReturnBehavior()],
            main: combat);
    }

    /// <summary>
    /// Обход ресурсов: выжить → (прервать каст без цели) → уйти вверх от опасного моба → пет → копать у текущей точки → защита
    /// (бьём только напавших) → лут → к следующей точке.
    /// </summary>
    private static BotBrain GatherResources(BrainContext context, IReadOnlyCollection<uint> tools, int routeStart)
    {
        var combat = new CombatBehavior(defendOnly: true);
        var route = new RouteBehavior(tools, routeStart);
        return new BotBrain(context,
            [new SurvivalBehavior(), new UselessCastBehavior(), new EscapeBehavior(route), new PetBehavior(), new GatherBehavior(tools, route), combat,
             new LootBehavior(), route],
            main: combat);
    }
}

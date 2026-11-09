using System;
using System.Collections.Generic;
using Notes.Core.Actions;
using Notes.Core.Clicker;
using Notes.Core.GameFiles;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.Stats;
using Notes.Core.World;

namespace Notes.Core.Brain;

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
        BotMode.LevelUp => "Прокачать друида",
        _ => "Бить мобов",
    };

    /// <summary>Собрать режим: мозг (<see cref="BotBrain"/>) один, режимы отличаются набором поведений и их порядком.</summary>
    /// <param name="routeStart">Обход: с какой точки начинать (с 0) — выбранная в окне.</param>
    /// <param name="random">Паузы и разброс; тесты задают свой, чтобы ход повторялся.</param>
    /// <param name="quests">Прокачка: где мобы и NPC; нет — без точек (идти можно только к тем, кого видно).</param>
    /// <param name="stats">Куда писать статистику (персонажа); нет — не ведём.</param>
    /// <param name="obstacles">Препятствия вокруг перса для обхода; нет — ходим как раньше.</param>
    public static IBotRunner Create(BotMode mode, ActionRunner runner, ClassSkills skills, BotSettings settings, ILogger log,
        IReadOnlyCollection<uint>? gatherTools = null, int routeStart = 0, Random? random = null, QuestGuide? quests = null,
        IStatsSink? stats = null, Navigation.ObstacleMap? obstacles = null)
    {
        // Копия настроек: окно может менять свои дальше (новые мозг получит через UpdateSettings)
        var context = new BrainContext(runner, skills, log, random ?? new Random())
        {
            Settings = settings.Clone(), Stats = stats, Obstacles = obstacles,
        };
        var tools = gatherTools ?? [];
        return mode switch
        {
            BotMode.GatherResources => GatherResources(context, tools, routeStart),
            BotMode.Clicker => Clicker(context),
            BotMode.LevelUp => LevelUp(context, quests ?? new QuestGuide(GuidePoints.Empty)),
            _ => FarmMobs(context, tools),
        };
    }

    /// <summary>
    /// Только ремонт у NPC или из сумки — тот же <see cref="RepairBehavior"/>, что в «Бить мобов»: проверка ремонта на живом
    /// клиенте (Probe repair). <paramref name="back"/> — после ремонта лететь обратно в центр фарма, как бот; нет — остаться у NPC.
    /// </summary>
    public static IBotRunner RepairOnly(ActionRunner runner, ClassSkills skills, BotSettings settings, ILogger log, bool back)
    {
        var context = new BrainContext(runner, skills, log, new Random()) { Settings = settings.Clone() };
        var repair = new RepairBehavior(c => back ? c.FarmSpot : null);
        return new BotBrain(context, [repair], main: repair);
    }

    /// <summary>Кликер: только сценарий из списка — без банок, пета и боя (банка — шагом с условием «HP меньше %»).</summary>
    private static BotBrain Clicker(BrainContext context)
    {
        var clicker = new ClickerBehavior();
        return new BotBrain(context, [clicker], main: clicker);
    }

    /// <summary>
    /// Прокачать друида: выжить → (прервать каст без цели) → пет → защита (бьём только напавших) → задания по журналу: бить мобов
    /// цели издалека, ходить «умно», брать и сдавать у NPC.
    /// </summary>
    private static BotBrain LevelUp(BrainContext context, QuestGuide guide)
    {
        var quests = new QuestBehavior(guide);
        return new BotBrain(context,
            [new SurvivalBehavior(), new UselessCastBehavior(), new PetBehavior(), new CombatBehavior(defendOnly: true), quests],
            main: quests);
    }

    /// <summary>
    /// Фарм мобов: после смерти → долететь до центра фарма («Старт» издалека) → слив опыта → выжить → (прервать каст без цели) → пет → убрать в банк (после лута) → копать ресурсы в радиусе фарма →
    /// бой → лут → вернуться в центр фарма. Первой — запись центра фарма в лог; ни она, ни прерывание каста хода не занимают.
    /// «После смерти» — встать в городе и вернуться: смерть ждали (слив опыта) — куда скажет слив, нет — в центр фарма, если включено
    /// «встать и вернуться»; пока встаёт и летит, ход у него.
    /// Слив опыта — за ним: пока он идёт, ход всегда у него, остальные не ходят (и пета не зовут, и банки не пьют).
    /// </summary>
    private static BotBrain FarmMobs(BrainContext context, IReadOnlyCollection<uint> tools)
    {
        var combat = new CombatBehavior();
        var afterDeath = new DeathReturnBehavior(c => c.FarmSpot);
        return new BotBrain(context,
            [new FarmCenterLog(), afterDeath, new RepairBehavior(c => c.FarmSpot), new StartTripBehavior(), new ExpDrainBehavior(new DeathDrain(afterDeath), c => c.FarmSpot), new SurvivalBehavior(), new UselessCastBehavior(), new PetBehavior(), new StashBehavior(), new GatherBehavior(tools),
             combat, new LootBehavior(), new ReturnBehavior()],
            main: combat);
    }

    /// <summary>
    /// Обход ресурсов: после смерти → выжить → (прервать каст без цели) → уйти вверх от опасного моба → пет → убрать в банк (после лута) → копать у текущей точки → защита
    /// (бьём только напавших, опасных — никогда) → лут → к следующей точке. «После смерти» — включено «встать и вернуться»: встать в городе и долететь
    /// до точки, у которой погибли (дальше обход как обычно); выключено — стоп; пока встаёт и летит, ход у него.
    /// </summary>
    private static BotBrain GatherResources(BrainContext context, IReadOnlyCollection<uint> tools, int routeStart)
    {
        var combat = new CombatBehavior(defendOnly: true, leaveDangerous: true);
        var route = new RouteBehavior(tools, routeStart);
        return new BotBrain(context,
            [new DeathReturnBehavior(route.AfterDeath), new SurvivalBehavior(), new UselessCastBehavior(), new EscapeBehavior(route), new PetBehavior(),
             new StashBehavior(), new GatherBehavior(tools, route), combat, new LootBehavior(), route],
            main: combat);
    }
}

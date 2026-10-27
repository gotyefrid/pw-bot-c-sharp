using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Clicker;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;
using BotCH.Tests.Fakes;

namespace BotCH.Tests.Clicker;

/// <summary>
/// Кликер на подставном мире для тестов: шаги → <see cref="Start"/> → снимки раз в 0,25 с (<see cref="Run"/>) — что ушло в
/// игру на каждом. Для проверки одного вида без сценария — условие на снимке (<see cref="Context"/>) и через файл настроек
/// (<see cref="RoundTrip{T}"/>).
/// </summary>
internal sealed class ClickerScript
{
    private int _ticks;

    public FakeWorld World { get; } = new();
    public FakeActions Game { get; } = new();
    public BotSettings Settings { get; } = new() { Mode = BotMode.Clicker };
    public List<LogEntry> Log { get; } = [];

    /// <summary>Почему бот попросил остановку; null — не просил.</summary>
    public string? Stopped { get; private set; }

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public ClickerScript Step(ClickerAction action, ClickerCondition? when = null)
    {
        Settings.Clicker.Steps.Add(new ClickerStep { Action = action, Condition = when ?? new AlwaysCondition() });
        return this;
    }

    public ClickerScript Key(string key, ClickerCondition? when = null) => Step(new PressKeyAction { Key = key }, when);

    public ClickerScript Pause(int milliseconds, ClickerCondition? when = null) => Step(new PauseAction { Milliseconds = milliseconds }, when);

    public IBotRunner Start()
    {
        var log = new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(Log));
        var bot = BotModes.Create(BotMode.Clicker, new ActionRunner(new GameControl(Game, Game), log.For("действия")),
            new ClassSkills(), Settings, log.For("мозг"));
        bot.StopRequested += reason => Stopped = reason;
        return bot;
    }

    /// <summary>Снимки раз в 0,25 с (самый первый — сразу); на каждом — что ушло в игру, через запятую.</summary>
    public List<string> Run(IBotRunner bot, int ticks)
    {
        var turns = new List<string>();
        for (var i = 0; i < ticks; i++)
        {
            var before = Game.Calls.Count;
            bot.Tick((_ticks++ == 0 ? World : World.Wait(0.25)).Snapshot());
            turns.Add(string.Join(", ", Game.Calls.Skip(before)));
        }

        return turns;
    }

    /// <summary>Ход мозга на этом снимке — проверить условие без сценария.</summary>
    public static BrainContext Context(WorldState world)
        => new(new ActionRunner(new GameControl(new FakeActions()), NullLogger.Instance), new ClassSkills(), NullLogger.Instance, new Random(1))
        {
            World = world,
        };

    /// <summary>Действие или условие — через файл настроек персонажа и обратно (каким его прочтёт бот).</summary>
    public static T RoundTrip<T>(T part) where T : class
    {
        var back = SettingsJson.Parse(Json(part)).Clicker.Steps.Single();
        return (T)(object)(part is ClickerAction ? back.Action : back.Condition);
    }

    /// <summary>Файл настроек с одним шагом, где это действие или условие.</summary>
    public static string Json(object part)
    {
        var step = part switch
        {
            ClickerAction action => new ClickerStep { Action = action },
            ClickerCondition condition => new ClickerStep { Condition = condition },
            _ => throw new ArgumentException("Не действие и не условие кликера", nameof(part)),
        };
        var settings = new BotSettings();
        settings.Clicker.Steps.Add(step);
        return SettingsJson.Serialize(settings);
    }

    /// <summary>Положить на землю предмет в <paramref name="distance"/> м от персонажа.</summary>
    public static void Drop(FakeWorld world, uint id, string name, float distance, GroundItemKind kind = GroundItemKind.Item, string? group = null)
        => world.Ground.Add(new GroundItem(0x1000 + id, id, 100 + id, kind, new Position(distance, 0, 0), name) { Group = group });
}

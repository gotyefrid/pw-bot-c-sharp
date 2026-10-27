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
using Xunit;

namespace BotCH.Tests.Brain;

/// <summary>Кликер на подставном мире: шаги сценария по кругу — что и когда уходит в игру.</summary>
public class ClickerScenarioTests
{
    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly BotSettings _settings = new() { Mode = BotMode.Clicker };
    private readonly List<LogEntry> _log = [];
    private string? _stopped;
    private int _ticks;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    private void Key(string key, ClickerCondition? when = null)
        => _settings.Clicker.Steps.Add(new ClickerStep { Action = new PressKeyAction { Key = key }, Condition = when ?? new AlwaysCondition() });

    private void Pause(int milliseconds, ClickerCondition? when = null)
        => _settings.Clicker.Steps.Add(new ClickerStep { Action = new PauseAction { Milliseconds = milliseconds }, Condition = when ?? new AlwaysCondition() });

    private IBotRunner Start()
    {
        var log = new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log));
        var bot = BotModes.Create(BotMode.Clicker, new ActionRunner(new GameControl(_actions, _actions), log.For("действия")),
            new ClassSkills(), _settings, log.For("мозг"));
        bot.StopRequested += reason => _stopped = reason;
        return bot;
    }

    // Снимки раз в 0,25 с, самый первый — сразу; что ушло в игру на каждом
    private List<string> Run(IBotRunner bot, int ticks)
    {
        var turns = new List<string>();
        for (var i = 0; i < ticks; i++)
        {
            var before = _actions.Calls.Count;
            bot.Tick((_ticks++ == 0 ? _world : _world.Wait(0.25)).Snapshot());
            turns.Add(string.Join(", ", _actions.Calls.Skip(before)));
        }

        return turns;
    }

    private void Drop(uint id, string name, float distance, GroundItemKind kind = GroundItemKind.Item, string? group = null)
        => _world.Ground.Add(new GroundItem(0x1000 + id, id, 100 + id, kind, new Position(distance, 0, 0), name) { Group = group });

    [Fact]
    public void KeysGoInOrderAndCircleEveryTurn()
    {
        // Без пауз круг — за снимок: каждая клавиша по разу, ни одна дважды
        Key("1");
        Key("2");
        Key("1");

        Assert.Equal(["key 1, key 2, key 1", "key 1, key 2, key 1"], Run(Start(), 2));
    }

    [Fact]
    public void PauseHoldsTheNextStep()
    {
        Key("F1");
        Pause(1000);
        Key("F2");
        var bot = Start();

        var turns = Run(bot, 6);

        // 0 с: F1, пауза пошла; 1 с: пауза кончилась — F2, и снова F1 с новой паузой
        Assert.Equal(["key F1", "", "", "", "key F2, key F1", ""], turns);
        Assert.Contains("пауза 1000 мс", bot.Status);
    }

    [Fact]
    public void StepWithFalseConditionIsSkipped()
    {
        Key("F1", new HpBelowCondition { Percent = 50 });
        Key("2");
        var bot = Start();

        Assert.Equal(["key 2", "key 2"], Run(bot, 2));

        _world.Hp = 200;
        Assert.Equal(["key F1, key 2"], Run(bot, 1));
    }

    [Fact]
    public void ConditionOfPauseCountsToo()
    {
        // «Пауза 5000 мс, если HP меньше 50 %» — пока HP полное, пауза не держит
        Key("F1");
        Pause(5000, new HpBelowCondition { Percent = 50 });

        Assert.Equal(["key F1", "key F1"], Run(Start(), 2));
    }

    [Fact]
    public void NothingToDoWaitsForNextSnapshot()
    {
        Key("F1", new HpBelowCondition { Percent = 50 });
        var bot = Start();

        Assert.Equal(["", ""], Run(bot, 2));
        Assert.Contains("жду", bot.Status);
    }

    [Fact]
    public void TargetConditionSeesAnyTarget()
    {
        // «Цели нет» — взять в таргет (Tab), «цель есть» — бить (1); цель — кто угодно, даже не из списка мобов рядом
        Key("Tab", new TargetCondition { Is = TargetIs.None });
        Key("1", new TargetCondition { Is = TargetIs.Any });
        var bot = Start();

        Assert.Equal(["key Tab"], Run(bot, 1));

        _world.TargetWid = _world.AddMob(0x80104298, "Волк", 10).Wid;
        Assert.Equal(["key 1"], Run(bot, 1));
        Assert.Contains(_log, e => e.Message.Contains("цель — Волк"));

        _world.TargetWid = 0x00AB0001;
        Assert.Equal(["key 1"], Run(bot, 1));
    }

    [Fact]
    public void ItemConditionLooksAtNamesGroupsAndRadius()
    {
        Key("4", new ItemNearbyCondition { Items = ["Мягкий мех", "Банки"], Radius = 10 });
        Drop(1, "Мягкий мех", 12);
        Drop(2, "Железная руда", 2, GroundItemKind.Resource);
        Drop(3, "Мягкий мех", 5, GroundItemKind.Resource);
        Drop(4, "Сломанный меч", 3);
        var bot = Start();

        // Далеко, ресурсы и чужое — не повод
        Assert.Equal(["", ""], Run(bot, 2));

        // Банка — по группе
        Drop(5, "Зелье здоровья", 6, group: "Банки");
        Assert.Equal(["key 4"], Run(bot, 1));
        Assert.Contains(_log, e => e.Message.Contains("Зелье здоровья"));
    }

    [Fact]
    public void EmptyItemListMeansAnyItem()
    {
        Key("5", new ItemNearbyCondition());
        Drop(1, "Монеты", 3, GroundItemKind.Money);

        Assert.Equal(["key 5"], Run(Start(), 1));
    }

    [Fact]
    public void UnreadyStepIsSkipped()
    {
        Key("");
        Key("F2");

        Assert.Equal(["key F2"], Run(Start(), 1));
        Assert.Null(_stopped);
    }

    [Fact]
    public void NothingReadyStopsTheBot()
    {
        Key("");
        Key("Alt+F4");

        Run(Start(), 1);

        Assert.Empty(_actions.Calls);
        Assert.Equal("в кликере нет ни одного готового действия", _stopped);
    }

    [Fact]
    public void FailedStepIsLoggedAndScriptGoesOn()
    {
        _actions.Refuse.Add("key F1");
        Key("F1");
        Key("F2");

        Assert.Equal(["key F1, key F2", "key F1, key F2"], Run(Start(), 2));
        Assert.Single(_log, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Кликер: шаг 1"));
    }

    [Fact]
    public void WindowSeesCurrentStep()
    {
        Key("F1");
        Pause(1000);
        var bot = Start();
        var progress = bot.Part<IClickerProgress>()!;

        Assert.Null(progress.Current);
        Run(bot, 1);
        Assert.Equal(1, progress.Current);

        bot.Reset();
        Assert.Null(progress.Current);
    }

    [Fact]
    public void StepsChangedOnTheGo()
    {
        // Шаги убрали, пока шла пауза третьего: доделать её и начать с первого
        Key("F1");
        Key("F2");
        Pause(500);
        var bot = Start();
        Run(bot, 1);

        _settings.Clicker.Steps.RemoveAt(1);
        bot.UpdateSettings(_settings);

        Assert.Equal(["", "key F1", ""], Run(bot, 3));
    }

    [Fact]
    public void DoesNothingElse()
    {
        // Ни банок, ни боя, ни пета: только сценарий
        _world.Hp = 50;
        _world.AddPotion(1, 8617, 5, hp: 300);
        _world.AddMob(1, "Волк", 5);
        Key("F3", new ItemNearbyCondition());

        Run(Start(), 8);

        Assert.Empty(_actions.Calls);
    }
}

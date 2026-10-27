using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Brain;

/// <summary>Кликер на подставном мире: какие клавиши и в каком порядке жмутся по правилам.</summary>
public class ClickerScenarioTests
{
    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly BotSettings _settings = new() { Mode = BotMode.Clicker };
    private readonly List<LogEntry> _log = [];
    private string? _stopped;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    private ClickerRule Rule(string key, ClickerCondition condition = ClickerCondition.Always, double repeat = 1)
    {
        var rule = new ClickerRule { Key = key, Condition = condition, RepeatSeconds = repeat };
        _settings.Clicker.Rules.Add(rule);
        return rule;
    }

    private IBotRunner Start()
    {
        var log = new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log));
        var bot = BotModes.Create(BotMode.Clicker, new ActionRunner(new GameControl(_actions, _actions), log.For("действия")),
            new ClassSkills(), _settings, log.For("мозг"));
        bot.StopRequested += reason => _stopped = reason;
        return bot;
    }

    // Снимки раз в 0,25 с, первый — сразу
    private void Run(IBotRunner bot, int ticks)
    {
        for (var i = 0; i < ticks; i++)
            bot.Tick((i == 0 ? _world : _world.Wait(0.25)).Snapshot());
    }

    private GroundItem Drop(uint id, string name, float distance, GroundItemKind kind = GroundItemKind.Item, string? group = null)
    {
        var item = new GroundItem(0x1000 + id, id, 100 + id, kind, new Position(distance, 0, 0), name) { Group = group };
        _world.Ground.Add(item);
        return item;
    }

    [Fact]
    public void AlwaysRuleIsPressedByItsRepeatTime()
    {
        Rule("3", repeat: 1);
        var bot = Start();

        Run(bot, 9);

        // 0 с, 1 с, 2 с
        Assert.Equal(["key 3", "key 3", "key 3"], _actions.Calls);
    }

    [Fact]
    public void TopRuleGoesFirstAndOnePressPerTurn()
    {
        // HP 40 %: обе подходят — сначала верхняя, следующим ходом нижняя (верхняя ждёт своего повтора)
        _world.Hp = 200;
        Rule("F1", ClickerCondition.HpBelow, repeat: 5).HpPercent = 50;
        Rule("2", repeat: 0.25);
        var bot = Start();

        Run(bot, 3);

        Assert.Equal(["key F1", "key 2", "key 2"], _actions.Calls);
    }

    [Fact]
    public void HpRuleWaitsForLowHp()
    {
        Rule("F1", ClickerCondition.HpBelow).HpPercent = 50;
        var bot = Start();

        Run(bot, 4);
        Assert.Empty(_actions.Calls);
        Assert.Contains("жду", bot.Status);

        _world.Hp = 240;
        Run(bot, 1);
        Assert.Equal(["key F1"], _actions.Calls);
        Assert.Contains("HP 48 %", bot.Status);
    }

    [Fact]
    public void ItemRuleLooksAtNamesGroupsAndRadius()
    {
        var rule = Rule("4", ClickerCondition.ItemNearby, repeat: 0.25);
        rule.Items = ["Мягкий мех", "Банки"];
        rule.ItemRadius = 10;
        Drop(1, "Мягкий мех", 12);
        Drop(2, "Железная руда", 2, GroundItemKind.Resource);
        Drop(3, "Мягкий мех", 5, GroundItemKind.Resource);
        Drop(4, "Сломанный меч", 3);
        var bot = Start();

        // Далеко, ресурсы и чужое — не повод
        Run(bot, 2);
        Assert.Empty(_actions.Calls);

        // Банка — по группе
        Drop(5, "Зелье здоровья", 6, group: "Банки");
        Run(bot, 1);
        Assert.Equal(["key 4"], _actions.Calls);
        Assert.Contains("Зелье здоровья", bot.Status);
    }

    [Fact]
    public void EmptyItemListMeansAnyItem()
    {
        Rule("5", ClickerCondition.ItemNearby).Items = [];
        Drop(1, "Монеты", 3, GroundItemKind.Money);
        var bot = Start();

        Run(bot, 1);

        Assert.Equal(["key 5"], _actions.Calls);
    }

    [Fact]
    public void RuleWithoutKeyIsSkipped()
    {
        Rule("");
        Rule("F2");
        var bot = Start();

        Run(bot, 1);

        Assert.Equal(["key F2"], _actions.Calls);
        Assert.Null(_stopped);
    }

    [Fact]
    public void NoRulesWithKeyStopsTheBot()
    {
        Rule("");
        var bot = Start();

        Run(bot, 1);

        Assert.Empty(_actions.Calls);
        Assert.Equal("у кликера нет правил с клавишей", _stopped);
    }

    [Fact]
    public void ConditionStartIsLoggedOncePressesQuietly()
    {
        // 4 нажатия F1 — в обычном логе одна строка «HP … — жму F1», остальное — подробный лог
        _world.Hp = 100;
        Rule("F1", ClickerCondition.HpBelow, repeat: 0.25).HpPercent = 50;
        var bot = Start();

        Run(bot, 4);

        Assert.Equal(4, _actions.Calls.Count);
        var info = _log.Where(e => e.Level >= LogLevel.Info).Select(e => e.Message).ToList();
        Assert.Equal(["Кликер: HP 20 % — меньше 50 % — жму F1 раз в 0,25 с"], info);

        // HP восстановилось и снова упало — снова строка
        _world.Hp = 500;
        Run(bot, 2);
        _world.Hp = 100;
        Run(bot, 1);
        Assert.Equal(2, _log.Count(e => e.Level >= LogLevel.Info));
    }

    [Fact]
    public void DoesNothingElse()
    {
        // Ни банок, ни боя, ни пета: только правила
        _world.Hp = 50;
        _world.AddPotion(1, 8617, 5, hp: 300);
        _world.AddMob(1, "Волк", 5);
        Rule("F3", ClickerCondition.ItemNearby);
        var bot = Start();

        Run(bot, 8);

        Assert.Empty(_actions.Calls);
    }
}

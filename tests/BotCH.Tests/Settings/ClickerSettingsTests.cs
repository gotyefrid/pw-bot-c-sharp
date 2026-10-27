using BotCH.Core.Settings;
using Xunit;

namespace BotCH.Tests.Settings;

/// <summary>Правила кликера в файле персонажа: что сохраняется и что приводится в порядок при чтении.</summary>
public class ClickerSettingsTests
{
    [Fact]
    public void RulesSurviveSaveAndLoad()
    {
        var s = new BotSettings();
        s.Clicker.Rules.Add(new ClickerRule { Key = "F1", Condition = ClickerCondition.HpBelow, HpPercent = 40, RepeatSeconds = 2.5 });
        s.Clicker.Rules.Add(new ClickerRule { Key = "Ctrl+2", Condition = ClickerCondition.ItemNearby, Items = ["Мягкий мех", "Банки"], ItemRadius = 8 });

        var json = SettingsJson.Serialize(s);
        var back = SettingsJson.Parse(json);

        Assert.Contains("\"condition\": \"HpBelow\"", json);
        Assert.Equal(2, back.Clicker.Rules.Count);
        var (hp, item) = (back.Clicker.Rules[0], back.Clicker.Rules[1]);
        Assert.Equal(("F1", ClickerCondition.HpBelow, 40, 2.5), (hp.Key, hp.Condition, hp.HpPercent, hp.RepeatSeconds));
        Assert.Equal(("Ctrl+2", 8), (item.Key, item.ItemRadius));
        Assert.Equal(["Мягкий мех", "Банки"], item.Items);
    }

    [Fact]
    public void OldFileWithoutClickerHasNoRules()
        => Assert.Empty(SettingsJson.Parse("""{ "mode": "Clicker" }""").Clicker.Rules);

    [Fact]
    public void BadValuesAreCleanedButRulesStay()
    {
        // Правило без клавиши — только что добавленное, ещё не настроено: не выкидывается
        var s = SettingsJson.Parse("""
            { "clicker": { "rules": [
                { "key": "" },
                { "key": "Alt+F4", "repeatSeconds": 0 },
                { "key": "ctrl+f2", "hpPercent": 500, "itemRadius": -3, "repeatSeconds": 99999, "items": [" Мех ", "мех", ""] },
                null
            ] } }
            """);

        var rules = s.Clicker.Rules;
        Assert.Equal(3, rules.Count);
        Assert.Equal(("", "", "Ctrl+F2"), (rules[0].Key, rules[1].Key, rules[2].Key));
        Assert.Null(rules[1].Stroke);
        Assert.Equal(ClickerRule.MinRepeat, rules[1].RepeatSeconds);
        Assert.Equal((100, 1, ClickerRule.MaxRepeat), (rules[2].HpPercent, rules[2].ItemRadius, rules[2].RepeatSeconds));
        Assert.Equal(["Мех"], rules[2].Items);
    }

    [Fact]
    public void ConditionIsDescribedForPlayer()
    {
        Assert.Equal("всегда", new ClickerRule().DescribeCondition());
        Assert.Equal("HP меньше 30 %", new ClickerRule { Condition = ClickerCondition.HpBelow, HpPercent = 30 }.DescribeCondition());
        Assert.Equal("лежит любой предмет (до 10 м)", new ClickerRule { Condition = ClickerCondition.ItemNearby }.DescribeCondition());
        Assert.Equal("лежит: Мех, Банки (до 5 м)",
            new ClickerRule { Condition = ClickerCondition.ItemNearby, Items = ["Мех", "Банки"], ItemRadius = 5 }.DescribeCondition());
    }

    [Fact]
    public void ReplacingRulesIsAnEdit()
    {
        var s = new BotSettings();
        var edits = 0;
        s.Edited += () => edits++;

        s.Clicker.Rules = [new ClickerRule { Key = "F1" }];

        Assert.Equal(1, edits);
    }
}

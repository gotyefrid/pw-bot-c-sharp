using System.Linq;
using Notes.Core.Clicker;
using Notes.Core.Settings;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>Условия шага: должны выполниться все, нет условий — всегда; старый файл с одним условием.</summary>
public class ClickerStepTests
{
    private readonly FakeWorld _world = new();

    private string? Check(ClickerStep step) => step.Check(ClickerScript.Context(_world.Snapshot()));

    private static ClickerStep Step(params ClickerCondition[] conditions) => new() { Conditions = new ConditionList(conditions) };

    [Fact]
    public void NoConditionsMeansAlways()
    {
        Assert.Empty(new ClickerStep().Conditions);
        Assert.Equal("всегда", Check(new ClickerStep()));
        Assert.Equal("всегда", new ClickerStep().DescribeConditions());
    }

    [Fact]
    public void AllConditionsMustHold()
    {
        // Цели нет, HP 80 %: «цели нет» выполнено, «HP меньше 50 %» — нет
        var step = Step(new TargetCondition { Is = TargetIs.None }, new HpBelowCondition { Percent = 50 });
        Assert.Null(Check(step));

        _world.Hp = 200;
        Assert.Equal("цели нет, HP 40 % — меньше 50 %", Check(step));
    }

    [Fact]
    public void DescribedWithAnd()
        => Assert.Equal("цель есть И HP меньше 50 %",
            Step(new TargetCondition { Is = TargetIs.Any }, new HpBelowCondition { Percent = 50 }).DescribeConditions());

    [Fact]
    public void OldFileWithOneConditionReadsIntoList()
    {
        var step = SettingsJson.Parse("""
            { "clicker": { "steps": [ { "action": { "kind": "PressKey", "key": "F1" }, "condition": { "kind": "HpBelow", "percent": 30 } } ] } }
            """).Clicker.Steps.Single();

        Assert.Equal(30, Assert.IsType<HpBelowCondition>(Assert.Single(step.Conditions)).Percent);
    }

    [Fact]
    public void ConditionsSurviveFileInOrder()
    {
        var settings = new BotSettings();
        settings.Clicker.Steps.Add(Step(new TargetCondition { Is = TargetIs.Any }, new HpBelowCondition { Percent = 20 }));

        var json = SettingsJson.Serialize(settings);
        var back = SettingsJson.Parse(json).Clicker.Steps.Single().Conditions;

        Assert.DoesNotContain("\"condition\":", json);
        Assert.Equal([typeof(TargetCondition), typeof(HpBelowCondition)], back.Select(c => c.GetType()));
    }

    [Fact]
    public void ListReportsEveryEditInside()
    {
        // Окно шага слушает список: добавили, убрали, заменили, поменяли поле у условия — сохранить и обновить строку
        var hp = new HpBelowCondition();
        var list = new ConditionList();
        var edits = 0;
        list.Edited += () => edits++;

        list.Add(hp);
        hp.Percent = 30;
        list[0] = new TargetCondition();
        hp.Percent = 40;
        list.RemoveAt(0);

        Assert.Equal(4, edits);
    }
}

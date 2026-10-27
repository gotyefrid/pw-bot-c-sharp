using BotCH.Core.Clicker;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Группа «Все условия (И)»: выполнено, когда выполнены все внутри; пустая — всегда.</summary>
public class AllConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(ClickerCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    private static AllCondition All(params ClickerCondition[] conditions) => new() { Conditions = new ConditionList(conditions) };

    [Fact]
    public void HoldsWhenAllHold()
    {
        var all = All(new TargetCondition { Is = TargetIs.None }, new HpBelowCondition { Percent = 50 });
        Assert.Null(Check(all));

        _world.Hp = 200;
        Assert.Equal("цели нет, HP 40 % — меньше 50 %", Check(all));
    }

    [Fact]
    public void EmptyIsAlways()
    {
        Assert.Equal("всегда", Check(new AllCondition()));
        Assert.Equal("всегда", new AllCondition().Describe());
    }

    [Fact]
    public void DescribedWithAndAndBracketsForInnerGroups()
    {
        var any = new AnyCondition { Conditions = { new HpBelowCondition { Percent = 30 }, new TargetCondition() } };

        Assert.Equal("цель есть И (HP меньше 30 % ИЛИ цели нет)", All(new TargetCondition { Is = TargetIs.Any }, any).Describe());
        Assert.Equal("НЕ (цель есть И HP меньше 50 %)",
            new AllCondition { Not = true, Conditions = { new TargetCondition { Is = TargetIs.Any }, new HpBelowCondition() } }.Describe());
    }

    [Fact]
    public void InnerEditIsEditOfGroup()
    {
        // Поменяли поле у условия внутри (и во вложенной группе) — группа говорит о правке: шаг сохранится
        var hp = new HpBelowCondition();
        var inner = new AnyCondition { Conditions = { hp } };
        var all = All(inner);
        var edits = 0;
        all.PropertyChanged += (_, e) => edits += e.PropertyName == nameof(AllCondition.Conditions) ? 1 : 0;

        hp.Percent = 30;
        inner.Conditions.Add(new TargetCondition());
        all.Conditions.RemoveAt(0);

        Assert.Equal(3, edits);
    }

    [Fact]
    public void FileKeepsNestedGroups()
    {
        var back = ClickerScript.RoundTrip(All(new TargetCondition { Is = TargetIs.Any },
            new AnyCondition { Not = true, Conditions = { new HpBelowCondition { Percent = 30 } } }));

        Assert.Equal(2, back.Conditions.Count);
        Assert.Equal(TargetIs.Any, Assert.IsType<TargetCondition>(back.Conditions[0]).Is);
        var any = Assert.IsType<AnyCondition>(back.Conditions[1]);
        Assert.True(any.Not);
        Assert.Equal(30, Assert.IsType<HpBelowCondition>(Assert.Single(any.Conditions)).Percent);
    }

    [Fact]
    public void FileCleansInside()
    {
        var back = ClickerScript.RoundTrip(All(new HpBelowCondition { Percent = 500 }));

        Assert.Equal(100, Assert.IsType<HpBelowCondition>(Assert.Single(back.Conditions)).Percent);
    }
}

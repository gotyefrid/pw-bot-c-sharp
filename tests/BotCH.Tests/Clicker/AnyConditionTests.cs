using BotCH.Core.Clicker;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Группа «Любое из условий (ИЛИ)»: выполнено, когда выполнено хоть одно внутри; пустая — никогда.</summary>
public class AnyConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(ClickerCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    private static AnyCondition Any(params ClickerCondition[] conditions) => new() { Conditions = new ConditionList(conditions) };

    [Fact]
    public void HoldsWhenAnyHolds()
    {
        // Цели нет, HP 80 %: первое не выполнено, второе — да; причина — того, что выполнено
        var any = Any(new HpBelowCondition { Percent = 50 }, new TargetCondition { Is = TargetIs.None });

        Assert.Equal("цели нет", Check(any));
    }

    [Fact]
    public void NotWhenNoneHolds()
        => Assert.Null(Check(Any(new HpBelowCondition { Percent = 50 }, new TargetCondition { Is = TargetIs.Any })));

    [Fact]
    public void EmptyIsNever()
    {
        // Пустая группа — шаг ещё не настроен: пусть не выполняется
        Assert.Null(Check(new AnyCondition()));
        Assert.Equal("никогда (нет условий)", new AnyCondition().Describe());
    }

    [Fact]
    public void DescribedWithOr()
        => Assert.Equal("HP меньше 30 % ИЛИ цели нет", Any(new HpBelowCondition { Percent = 30 }, new TargetCondition()).Describe());

    [Fact]
    public void FileKeepsInside()
    {
        var back = ClickerScript.RoundTrip(Any(new TargetCondition { Is = TargetIs.Any }, new HpBelowCondition { Percent = 20 }));

        Assert.Equal(2, back.Conditions.Count);
        Assert.IsType<TargetCondition>(back.Conditions[0]);
        Assert.Equal(20, Assert.IsType<HpBelowCondition>(back.Conditions[1]).Percent);
    }
}

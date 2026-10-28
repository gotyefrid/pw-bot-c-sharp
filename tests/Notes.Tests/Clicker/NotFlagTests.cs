using Notes.Core.Clicker;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>Галочка «Наоборот (НЕ)» — есть у любого условия (общая часть всех условий).</summary>
public class NotFlagTests
{
    private readonly FakeWorld _world = new();

    private string? Check(ClickerCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    [Fact]
    public void TurnsResultOver()
    {
        // Цели нет: «цель есть» — не выполнено, «НЕ цель есть» — выполнено
        Assert.Null(Check(new TargetCondition { Is = TargetIs.Any }));
        Assert.Equal("НЕ (цель есть)", Check(new TargetCondition { Is = TargetIs.Any, Not = true }));

        _world.TargetWid = _world.AddMob(0x80104298, "Волк", 10).Wid;
        Assert.Null(Check(new TargetCondition { Is = TargetIs.Any, Not = true }));
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("НЕ (HP меньше 50 %)", new HpBelowCondition { Not = true }.Describe());
        Assert.Equal("HP меньше 50 %", new HpBelowCondition().Describe());
    }

    [Fact]
    public void FileHasItOnlyWhenOn()
    {
        Assert.DoesNotContain("\"not\"", ClickerScript.Json(new HpBelowCondition()));
        Assert.Contains("\"not\": true", ClickerScript.Json(new HpBelowCondition { Not = true }));
        Assert.True(ClickerScript.RoundTrip(new HpBelowCondition { Not = true }).Not);
        Assert.False(ClickerScript.RoundTrip(new HpBelowCondition()).Not);
    }

    [Fact]
    public void EditChangesDescriptionToo()
    {
        // Список в окне показывает подпись условия: «НЕ» включили — подпись поменялась
        var condition = new HpBelowCondition();
        var changed = new System.Collections.Generic.List<string?>();
        condition.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        condition.Not = true;

        Assert.Equal(2, changed.Count);
        Assert.Contains(nameof(ClickerCondition.Not), changed);
        Assert.Contains(nameof(ClickerCondition.Description), changed);
    }
}

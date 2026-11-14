using System.Collections.Generic;
using System.Linq;
using Notes.Core.Settings;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.Settings;

/// <summary>Связка скиллов в окне: текст строки, проверки, условие по умолчанию, перестановка строк, список скиллов.</summary>
public class ComboEditingTests
{
    private static SkillInfo Skill(int id, string? name) => new(id, 1, 0, 0, 0, name);

    [Fact]
    public void ConditionsJoinedWithAnd()
    {
        var row = new ComboRow
        {
            SkillId = 1,
            Conditions =
            [
                new ComboCondition { Check = ComboCheck.MobHp, Below = true, Value = 30 },
                new ComboCondition { Check = ComboCheck.SkillReady, SkillId = 7, SkillName = "Молния" },
            ],
        };

        Assert.Equal("HP моба < 30% и готов «Молния»", ComboEditing.ConditionsText(row));
    }

    [Fact]
    public void NoConditionsIsAlways()
        => Assert.Equal("всегда", ComboEditing.ConditionsText(new ComboRow { SkillId = 1 }));

    [Theory]
    [InlineData(ComboRepeat.Always, 10, "каждый раз")]
    [InlineData(ComboRepeat.OncePerFight, 10, "1 раз за бой")]
    [InlineData(ComboRepeat.Interval, 15, "не чаще 15 с")]
    public void RepeatText(ComboRepeat repeat, int seconds, string text)
        => Assert.Equal(text, ComboEditing.RepeatText(new ComboRow { SkillId = 1, Repeat = repeat, IntervalSeconds = seconds }));

    [Fact]
    public void EveryCheckListedOnceWithTitle()
    {
        var all = System.Enum.GetValues(typeof(ComboCheck)).Cast<ComboCheck>().ToList();

        Assert.Equal(all.OrderBy(c => c), ComboEditing.Checks.OrderBy(c => c));
        Assert.All(all, c => Assert.NotEqual(c.ToString(), ComboEditing.CheckTitle(c)));
        Assert.Equal(all.Count, all.Select(ComboEditing.CheckTitle).Distinct().Count());
    }

    [Fact]
    public void NewConditionHasStartingNumber()
    {
        var hp = ComboEditing.NewCondition(ComboCheck.MobHp);
        Assert.True(hp.Below);
        Assert.Equal(30, hp.Value);

        var ready = ComboEditing.NewCondition(ComboCheck.SkillReady, 7, "Молния");
        Assert.Equal(7, ready.SkillId);
        Assert.Equal("Молния", ready.SkillName);
    }

    [Fact]
    public void ChangeCheckKeepsNumberOfSameKind()
    {
        var condition = new ComboCondition { Check = ComboCheck.MobHp, Below = false, Value = 70 };

        ComboEditing.ChangeCheck(condition, ComboCheck.MyHp);

        Assert.Equal(ComboCheck.MyHp, condition.Check);
        Assert.False(condition.Below);
        Assert.Equal(70, condition.Value);
    }

    [Fact]
    public void ChangeCheckToOtherKindTakesDefaults()
    {
        var condition = new ComboCondition { Check = ComboCheck.SkillReady, SkillId = 7, SkillName = "Молния" };

        ComboEditing.ChangeCheck(condition, ComboCheck.Distance);

        Assert.Equal(ComboCheck.Distance, condition.Check);
        Assert.True(condition.Below);
        Assert.Equal(5, condition.Value);
    }

    [Fact]
    public void ChangeCheckBetweenSkillChecksKeepsSkill()
    {
        var condition = new ComboCondition { Check = ComboCheck.SkillReady, SkillId = 7, SkillName = "Молния" };

        ComboEditing.ChangeCheck(condition, ComboCheck.LastSkill);

        Assert.Equal(7, condition.SkillId);
        Assert.Equal("Молния", condition.SkillName);
    }

    [Fact]
    public void CopyIsIndependent()
    {
        var row = new ComboRow
        {
            SkillId = 1, SkillName = "Удар", Enabled = false, WaitReady = true, Repeat = ComboRepeat.Interval, IntervalSeconds = 15,
            Conditions = [new ComboCondition { Check = ComboCheck.MobHp, Below = true, Value = 30 }],
        };

        var copy = ComboEditing.Copy(row);
        copy.Conditions[0].Value = 50;
        copy.Conditions.Add(new ComboCondition { Check = ComboCheck.MyMp, Value = 10 });

        Assert.Equal((1, "Удар", false, true, ComboRepeat.Interval, 15), (copy.SkillId, copy.SkillName, copy.Enabled, copy.WaitReady, copy.Repeat, copy.IntervalSeconds));
        Assert.Single(row.Conditions);
        Assert.Equal(30, row.Conditions[0].Value);
    }

    [Fact]
    public void ApplyTakesSettingsButNotEnabled()
    {
        var row = new ComboRow { SkillId = 1, SkillName = "Удар", Enabled = false };
        var edited = new ComboRow
        {
            SkillId = 7, SkillName = "Молния", Enabled = true, WaitReady = true, Repeat = ComboRepeat.OncePerFight, IntervalSeconds = 20,
            Conditions = [new ComboCondition { Check = ComboCheck.SkillReady, SkillId = 7, SkillName = "Молния" }],
        };

        ComboEditing.Apply(row, edited);

        Assert.Equal((7, "Молния", false, true, ComboRepeat.OncePerFight, 20), (row.SkillId, row.SkillName, row.Enabled, row.WaitReady, row.Repeat, row.IntervalSeconds));
        Assert.Equal("готов «Молния»", ComboEditing.ConditionsText(row));
        Assert.NotSame(edited.Conditions[0], row.Conditions[0]);
    }

    [Fact]
    public void MoveUpAndDown()
    {
        var items = new List<string> { "a", "b", "c" };

        Assert.Equal(0, ComboEditing.Move(items, 1, -1));
        Assert.Equal(["b", "a", "c"], items);
        Assert.Equal(2, ComboEditing.Move(items, 1, 1));
        Assert.Equal(["b", "c", "a"], items);
    }

    [Fact]
    public void MoveOutsideDoesNothing()
    {
        var items = new List<string> { "a", "b" };

        Assert.Equal(-1, ComboEditing.Move(items, 0, -1));
        Assert.Equal(-1, ComboEditing.Move(items, 1, 1));
        Assert.Equal(-1, ComboEditing.Move(items, 5, -1));
        Assert.Equal(["a", "b"], items);
    }

    [Fact]
    public void InGameLearnedSkillsThenMissingFromRows()
    {
        var rows = new List<ComboRow>
        {
            new() { SkillId = 2, SkillName = "Старое" },
            new() { SkillId = 9, SkillName = "Забытый", Conditions = [new ComboCondition { Check = ComboCheck.LastSkill, SkillId = 8, SkillName = "Другой" }] },
        };

        var skills = ComboEditing.Skills([Skill(1, "Удар"), Skill(2, null), Skill(3, "Лечение")], rows, id => id != 3);

        Assert.Equal(
        [
            new ComboSkill(1, "Удар", false),
            new ComboSkill(2, "Старое", false),
            new ComboSkill(9, "Забытый", true),
            new ComboSkill(8, "Другой", true),
        ], skills);
    }

    [Fact]
    public void NotInGameOnlySkillsFromRows()
    {
        var rows = new List<ComboRow>
        {
            new() { SkillId = 5, SkillName = "Молния" },
            new() { SkillId = 4, SkillName = "" },
            new() { SkillId = 5, SkillName = "Молния" },
        };

        var skills = ComboEditing.Skills(null, rows);

        Assert.Equal([new ComboSkill(5, "Молния", false), new ComboSkill(4, "скилл 4", false)], skills);
    }
}

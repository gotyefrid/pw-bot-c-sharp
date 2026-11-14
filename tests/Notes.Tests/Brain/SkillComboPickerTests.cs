using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Brain;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>Выбор строки связки скиллов: сверху вниз первая подходящая, «ждать», повторы, условия.</summary>
public class SkillComboPickerTests
{
    private readonly FakeWorld _world = new();
    private readonly SkillComboPicker _picker = new();
    private readonly NpcInfo _mob;

    public SkillComboPickerTests()
    {
        _mob = _world.AddMob(0x80000001, "Волк", 5, hp: 100);
        _world.Replace(_mob, m => m with { MaxHp = 200 }); // 50 %
        _world.TargetWid = _mob.Wid;
    }

    private static ComboRow Row(int skill, params ComboCondition[] conditions)
        => new() { SkillId = skill, SkillName = $"с{skill}", Conditions = conditions.ToList() };

    private static ComboCondition Num(ComboCheck check, bool below, int value) => new() { Check = check, Below = below, Value = value };

    private static ComboCondition Skill(ComboCheck check, int skill) => new() { Check = check, SkillId = skill };

    private void Cool(int skill, int ms)
    {
        var i = _world.Skills.FindIndex(s => s.Id == skill);
        _world.Skills[i] = _world.Skills[i] with { CooldownLeftMs = ms };
    }

    private ComboChoice Pick(IReadOnlyList<ComboRow> rows, double fightSeconds = 0)
    {
        var w = _world.Snapshot();
        return _picker.Pick(rows, w, w.Npcs.Single(n => n.Wid == _mob.Wid), TimeSpan.FromSeconds(fightSeconds), w.Time);
    }

    private int? Picked(IReadOnlyList<ComboRow> rows, double fightSeconds = 0) => Pick(rows, fightSeconds).Row?.SkillId;

    private void Press(IReadOnlyList<ComboRow> rows)
    {
        var choice = Pick(rows);
        Assert.NotNull(choice.Row);
        _picker.Pressed(choice, _world.Snapshot().Time);
    }

    [Fact]
    public void TopReadyRowWins()
    {
        _world.AddSkill(1);
        _world.AddSkill(2);
        ComboRow[] rows = [Row(1), Row(2)];

        Assert.Equal(1, Picked(rows));
        Cool(1, 2000);
        Assert.Equal(2, Picked(rows));
    }

    [Fact]
    public void DisabledAndUnlearnedRowsAreSkipped()
    {
        _world.AddSkill(2);
        _world.AddSkill(3);
        var off = Row(2);
        off.Enabled = false;

        var choice = Pick([Row(1), off, Row(3)]);

        Assert.Equal(3, choice.Row?.SkillId);
        Assert.Equal(2, choice.Index);
        Assert.Contains(choice.Skipped, s => s.Contains("не изучен"));
    }

    [Fact]
    public void WaitRowStopsSearchWhileCooling()
    {
        _world.AddSkill(1, cooldownLeftMs: 1500);
        _world.AddSkill(2);
        var first = Row(1);
        first.WaitReady = true;

        var choice = Pick([first, Row(2)]);

        Assert.Null(choice.Row);
        Assert.Contains("жду «с1»", choice.Waiting);
    }

    [Fact]
    public void WaitRowWithFailedConditionIsSkipped()
    {
        // «Ждать» — только когда условия выполнены: HP моба 50 %, а строка — для добивания
        _world.AddSkill(1, cooldownLeftMs: 1500);
        _world.AddSkill(2);
        var finisher = Row(1, Num(ComboCheck.MobHp, below: true, 30));
        finisher.WaitReady = true;

        Assert.Equal(2, Picked([finisher, Row(2)]));
    }

    [Fact]
    public void NumberConditions()
    {
        _world.AddSkill(1);
        _world.Hp = 100; // 20 %
        _world.Mp = 400; // 80 %

        Assert.Equal(1, Picked([Row(1, Num(ComboCheck.MobHp, below: true, 60))]));
        Assert.Null(Picked([Row(1, Num(ComboCheck.MobHp, below: false, 60))]));
        Assert.Equal(1, Picked([Row(1, Num(ComboCheck.MyHp, below: true, 30))]));
        Assert.Equal(1, Picked([Row(1, Num(ComboCheck.MyMp, below: false, 50))]));
        Assert.Equal(1, Picked([Row(1, Num(ComboCheck.Distance, below: true, 6))]));
        Assert.Null(Picked([Row(1, Num(ComboCheck.Distance, below: true, 4))]));
        Assert.Null(Picked([Row(1, Num(ComboCheck.FightSeconds, below: false, 10))], fightSeconds: 5));
        Assert.Equal(1, Picked([Row(1, Num(ComboCheck.FightSeconds, below: false, 10))], fightSeconds: 11));
    }

    [Fact]
    public void AllConditionsMustHold()
    {
        _world.AddSkill(1);
        var row = Row(1, Num(ComboCheck.MobHp, below: true, 60), Num(ComboCheck.Distance, below: true, 4));

        var choice = Pick([row]);

        Assert.Null(choice.Row);
        Assert.Contains(choice.Skipped, s => s.Contains("до моба < 4 м"));
    }

    [Fact]
    public void UnknownValueIsNotMet()
    {
        // Максимум MP на сервере не найден, HP моба не читается — условие не выполнено, а не «всегда»
        _world.AddSkill(1);
        _world.MaxMp = null;
        _world.Replace(_mob, m => m with { MaxHp = 0 });

        Assert.Null(Picked([Row(1, Num(ComboCheck.MyMp, below: true, 100))]));
        var choice = Pick([Row(1, Num(ComboCheck.MobHp, below: true, 100))]);
        Assert.Null(choice.Row);
        Assert.Contains(choice.Skipped, s => s.Contains("неизвестно"));
    }

    [Fact]
    public void SkillConditions()
    {
        _world.AddSkill(1);
        _world.AddSkill(2, cooldownLeftMs: 1000);

        Assert.Null(Picked([Row(1, Skill(ComboCheck.SkillReady, 2))]));
        Assert.Equal(1, Picked([Row(1, Skill(ComboCheck.SkillCooling, 2))]));
        Cool(2, 0);
        Assert.Equal(1, Picked([Row(1, Skill(ComboCheck.SkillReady, 2))]));
    }

    [Fact]
    public void LastSkillChains()
    {
        // «После 1 — 2»: строка 2 выше, но ждёт, пока последним нажат 1
        _world.AddSkill(1);
        _world.AddSkill(2);
        ComboRow[] rows = [Row(2, Skill(ComboCheck.LastSkill, 1)), Row(1)];

        Assert.Equal(1, Picked(rows));
        Press(rows);
        Assert.Equal(2, Picked(rows));
        Press(rows);
        Assert.Equal(1, Picked(rows));

        _picker.NewFight();
        Assert.Equal(1, Picked(rows));
    }

    [Fact]
    public void OncePerFight()
    {
        _world.AddSkill(1);
        _world.AddSkill(2);
        var opener = Row(1);
        opener.Repeat = ComboRepeat.OncePerFight;
        ComboRow[] rows = [opener, Row(2)];

        Press(rows);
        Assert.Equal(2, Picked(rows));

        _picker.NewFight();
        Assert.Equal(1, Picked(rows));
    }

    [Fact]
    public void IntervalCountsAcrossFights()
    {
        _world.AddSkill(1);
        _world.AddSkill(2);
        var buff = Row(1);
        buff.Repeat = ComboRepeat.Interval;
        buff.IntervalSeconds = 30;
        ComboRow[] rows = [buff, Row(2)];

        Press(rows);
        _picker.NewFight();
        _world.Wait(29);
        Assert.Equal(2, Picked(rows));
        _world.Wait(2);
        Assert.Equal(1, Picked(rows));
    }

    [Fact]
    public void CleanDropsEmptyAndClamps()
    {
        var rows = ComboRow.Clean([null, new ComboRow { SkillId = 0 }, new ComboRow { SkillId = 5, IntervalSeconds = 0, Conditions = null! }]);

        var row = Assert.Single(rows);
        Assert.Equal(1, row.IntervalSeconds);
        Assert.Empty(row.Conditions);
    }
}

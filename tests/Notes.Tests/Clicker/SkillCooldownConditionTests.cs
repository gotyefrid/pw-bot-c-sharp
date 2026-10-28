using Notes.Core.Clicker;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>Условие «Скилл на перезарядке»: по перезарядке из игры; «готов» — с «НЕ»; не выбран или не изучен — не выполнено.</summary>
public class SkillCooldownConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(SkillCooldownCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    private static SkillCooldownCondition Fireball(bool not = false) => new() { Skill = 101, SkillName = "Огненный шар", Not = not };

    [Fact]
    public void OnCooldown()
    {
        _world.AddSkill(101, cooldownLeftMs: 2500);

        Assert.Equal($"«Огненный шар» на перезарядке ещё {2.5:0.0} с", Check(Fireball()));
        Assert.Null(Check(Fireball(not: true)));
    }

    [Fact]
    public void ReadyIsNotOnCooldown()
    {
        _world.AddSkill(101);

        Assert.Null(Check(Fireball()));
        Assert.Equal("НЕ («Огненный шар» на перезарядке)", Check(Fireball(not: true)));
    }

    [Fact]
    public void NotLearnedOrNotChosenIsNotOnCooldown()
    {
        _world.AddSkill(102, cooldownLeftMs: 2500);

        Assert.Null(Check(Fireball()));
        Assert.Null(Check(new SkillCooldownCondition()));
    }

    [Fact]
    public void FileKeepsSkillAndName()
    {
        var back = ClickerScript.RoundTrip(new SkillCooldownCondition { Skill = 101, SkillName = " Огненный шар " });

        Assert.Equal(101, back.Skill);
        Assert.Equal("Огненный шар", back.SkillName);
        Assert.Equal(0, ClickerScript.RoundTrip(new SkillCooldownCondition { Skill = -5 }).Skill);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("скилл не выбран", new SkillCooldownCondition().Describe());
        Assert.Equal("«Огненный шар» на перезарядке", Fireball().Describe());
        Assert.Equal("скилл 101 на перезарядке", new SkillCooldownCondition { Skill = 101 }.Describe());
    }
}

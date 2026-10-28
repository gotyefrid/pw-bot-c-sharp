using Notes.Core.Clicker;
using Notes.Core.Logging;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Использовать скилл»: как кнопкой (по текущей цели), итог не ждёт. На перезарядке — ничего не шлёт; не изучен —
/// «не вышло»; свой каст не сбивает — ждёт его конца.
/// </summary>
public class UseSkillActionTests
{
    private readonly ClickerScript _script = new();

    private static UseSkillAction Fireball => new() { Skill = 101, SkillName = "Огненный шар" };

    private string[] WithKey(UseSkillAction action, int ticks = 1) => [.. _script.Step(action).Key("F1").Run(_script.Start(), ticks)];

    [Fact]
    public void AsButtonOnCurrentTargetAndGoesOn()
    {
        _script.World.AddSkill(101);

        Assert.Equal(["apply 101 0, key F1"], WithKey(Fireball));
    }

    [Fact]
    public void OnCooldownSendsNothing()
    {
        _script.World.AddSkill(101, cooldownLeftMs: 4000);

        Assert.Equal(["key F1"], WithKey(Fireball));
        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Debug && e.Message.Contains("«Огненный шар» на перезарядке ещё 4"));
    }

    [Fact]
    public void NotLearnedIsReported()
    {
        Assert.Equal(["key F1"], WithKey(Fireball));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("«Огненный шар» — не изучен"));
    }

    [Fact]
    public void NotChosenIsProblem() => Assert.Equal("скилл не выбран", new UseSkillAction().Problem);

    [Fact]
    public void WaitsForOwnCast()
    {
        _script.World.AddSkill(101);
        _script.World.Casting = true;
        var bot = _script.Step(Fireball).Key("F1").Start();

        Assert.Equal(["", ""], _script.Run(bot, 2));

        _script.World.Casting = false;
        Assert.Equal(["apply 101 0, key F1"], _script.Run(bot, 1));
    }

    [Fact]
    public void FileKeepsSkillAndName()
    {
        var back = ClickerScript.RoundTrip(new UseSkillAction { Skill = 101, SkillName = " Огненный шар " });

        Assert.Equal(101, back.Skill);
        Assert.Equal("Огненный шар", back.SkillName);
        Assert.Equal(0, ClickerScript.RoundTrip(new UseSkillAction { Skill = -5 }).Skill);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("скилл не выбран", new UseSkillAction().Describe());
        Assert.Equal("использовать «Огненный шар»", Fireball.Describe());
        Assert.Equal("использовать скилл 101", new UseSkillAction { Skill = 101 }.Describe());
    }
}

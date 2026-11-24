using Notes.Core.Clicker;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>Условие «Эффект моба в цели»: выбранный эффект или любой; «нет» — с «НЕ».</summary>
public class TargetEffectConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(int effect, bool not = false)
        => new TargetEffectCondition { Effect = effect, Not = not }.Check(ClickerScript.Context(_world.Snapshot()));

    private void Target(int effect)
    {
        var mob = _world.AddMob(0x80104298, "Призрак", 10);
        _world.Replace(mob, m => m with { Effect = effect });
        _world.TargetWid = mob.Wid;
    }

    [Fact]
    public void ChosenEffect()
    {
        Target(4);

        Assert.Equal("у цели Призрак «Усиленная магическая защита»", Check(4));
        Assert.Null(Check(MobEffects.Berserk));
    }

    [Fact]
    public void AnyEffect()
    {
        Target(MobEffects.Berserk);

        Assert.Equal("у цели Призрак «Берсерк»", Check(0));
    }

    [Fact]
    public void NoEffectWithNot()
    {
        // «Нет Усиленной магической защиты» — у моба без эффекта выполнено
        Target(0);

        Assert.Null(Check(0));
        Assert.NotNull(Check(4, not: true));
    }

    [Fact]
    public void NoTargetIsNotMet() => Assert.Null(Check(0));

    [Fact]
    public void NewConditionIsStrongAttack() => Assert.Equal(MobEffects.StrongAttack, new TargetEffectCondition().Effect);

    [Fact]
    public void FileKeepsEffect()
        => Assert.Equal(MobEffects.Berserk, ClickerScript.RoundTrip(new TargetEffectCondition { Effect = MobEffects.Berserk }).Effect);

    [Fact]
    public void Described()
    {
        Assert.Equal("у цели «Берсерк»", new TargetEffectCondition { Effect = MobEffects.Berserk }.Describe());
        Assert.Equal("НЕ (у цели любой эффект)", new TargetEffectCondition { Effect = 0, Not = true }.Describe());
    }
}

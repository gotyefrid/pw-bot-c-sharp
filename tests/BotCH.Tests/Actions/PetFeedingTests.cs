using BotCH.Core.Actions;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Actions;

public class PetFeedingTests
{
    private readonly FakeWorld _world = new();
    private readonly PetFeeding _feeding = new();

    public PetFeedingTests()
    {
        _world.SetPet(1, hunger: 1);
        _world.AddFood(0, 12842, 150, loyalty: 10);
        _world.AddFood(5, 12843, 20, loyalty: 50);
    }

    [Fact]
    public void WeakestFoodFirst()
    {
        Assert.Equal(12842u, _feeding.Choose(_world.Snapshot(), out _)!.Tid);
    }

    [Fact]
    public void NotHungryOrNoPetMeansNothing()
    {
        _world.SetPet(1, hunger: 0);
        Assert.Null(_feeding.Choose(_world.Snapshot(), out var why));
        Assert.Equal("", why);

        _world.Pet = null; // не друид
        Assert.Null(_feeding.Choose(_world.Snapshot(), out why));
        Assert.Equal("", why);
    }

    [Fact]
    public void PauseAfterSuccessEvenIfStillHungry()
    {
        // Сытость обновляется не сразу: следующие снимки «голоден», но корм не отправляется ~30 с
        _feeding.Report(12842, eaten: true, _world.Time);

        Assert.Null(_feeding.Choose(_world.Wait(5).Snapshot(), out var why));
        Assert.Contains("пауза", why);
        Assert.Null(_feeding.Choose(_world.Wait(20).Snapshot(), out _));
        Assert.NotNull(_feeding.Choose(_world.Wait(6).Snapshot(), out _));
    }

    [Fact]
    public void PauseAfterRefusalIsShorter()
    {
        _feeding.Report(12842, eaten: false, _world.Time);

        Assert.Null(_feeding.Choose(_world.Wait(9).Snapshot(), out _));
        Assert.NotNull(_feeding.Choose(_world.Wait(2).Snapshot(), out _));
    }

    [Fact]
    public void RefusalOfEatenFoodIsNotDislike()
    {
        _feeding.Choose(_world.Snapshot(), out _);
        _feeding.Report(12842, eaten: true, _world.Time);
        for (var i = 0; i < 5; i++)
            _feeding.Report(12842, eaten: false, _world.Wait(40).Time);

        Assert.Equal(12842u, _feeding.Choose(_world.Wait(40).Snapshot(), out _)!.Tid);
        Assert.Empty(_feeding.NotEaten);
    }

    [Fact]
    public void TwoRefusalsOfNeverEatenFoodMeanDislike()
    {
        _feeding.Choose(_world.Snapshot(), out _);
        _feeding.Report(12842, eaten: false, _world.Time);
        Assert.Equal(12842u, _feeding.Choose(_world.Wait(11).Snapshot(), out _)!.Tid); // один отказ — ещё пробуем

        _feeding.Report(12842, eaten: false, _world.Time);

        Assert.Equal(12843u, _feeding.Choose(_world.Wait(11).Snapshot(), out _)!.Tid);
        Assert.Contains(12842u, _feeding.NotEaten);
    }

    [Fact]
    public void OneRefusalDoesNotMeanNoFood()
    {
        // Отказ не приводит к «нет корма»
        _world.Bag.RemoveAt(1);
        _feeding.Choose(_world.Snapshot(), out _);
        _feeding.Report(12842, eaten: false, _world.Time);

        Assert.NotNull(_feeding.Choose(_world.Wait(11).Snapshot(), out _));
    }

    [Fact]
    public void ResummonedPetKeepsMemoryButOtherCageForgets()
    {
        _feeding.Choose(_world.Snapshot(), out _);
        _feeding.Report(12842, eaten: false, _world.Time);
        _feeding.Report(12842, eaten: false, _world.Time);

        // Тот же пет призван заново — WID другой, клетка та же: память остаётся
        _world.SetPet(1, hunger: 1);
        Assert.Equal(12843u, _feeding.Choose(_world.Wait(11).Snapshot(), out _)!.Tid);

        // Другой пет (другая клетка) — всё забыто
        _world.SetPet(2, hunger: 1);
        Assert.Equal(12842u, _feeding.Choose(_world.Snapshot(), out _)!.Tid);
    }

    [Fact]
    public void NoEdibleFoodIsExplained()
    {
        _world.Bag.Clear();

        Assert.Null(_feeding.Choose(_world.Snapshot(), out var why));
        Assert.Equal("нет корма в сумке", why);
    }
}

public class PotionPolicyTests
{
    private readonly FakeWorld _world = new();
    private readonly PotionPolicy _policy = new();

    [Fact]
    public void WeakestSuitableByLevel()
    {
        _world.Level = 4;
        _world.AddPotion(1, 8618, 25, hp: 80, level: 5); // не по уровню
        _world.AddPotion(2, 8617, 2, hp: 30);
        _world.AddPotion(3, 8616, 5, hp: 50);
        _world.AddPotion(4, 8647, 1, mp: 40);

        Assert.Equal(8617u, _policy.Choose(_world.Snapshot(), PotionKind.Hp, out _)!.Tid);
        Assert.Equal(8647u, _policy.Choose(_world.Snapshot(), PotionKind.Mp, out _)!.Tid);
    }

    [Fact]
    public void SameKindNotDrunkWhileActive()
    {
        var hp = _world.AddPotion(2, 8617, 2, hp: 30, seconds: 10);
        _world.AddPotion(4, 8647, 1, mp: 40);

        _policy.Drunk(hp, PotionKind.Hp, _world.Time);

        Assert.Null(_policy.Choose(_world.Wait(9).Snapshot(), PotionKind.Hp, out var why));
        Assert.Contains("действует", why);
        Assert.NotNull(_policy.Choose(_world.Snapshot(), PotionKind.Mp, out _));
        Assert.NotNull(_policy.Choose(_world.Wait(1.5).Snapshot(), PotionKind.Hp, out _));
    }

    [Fact]
    public void HpPotionCooldownRespected()
    {
        _world.AddPotion(2, 8617, 2, hp: 30);
        _world.HpPotionReady = false;

        Assert.Null(_policy.Choose(_world.Snapshot(), PotionKind.Hp, out var why));
        Assert.Contains("перезарядке", why);
    }

    [Fact]
    public void NoPotions()
    {
        Assert.Null(_policy.Choose(_world.Snapshot(), PotionKind.Mp, out var why));
        Assert.Equal("нет банок Mp", why);
    }
}

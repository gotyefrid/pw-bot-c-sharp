using BotCH.Core.Clicker;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «Пет голоден»: смотрит на призванного пета — то же правило, по которому бот кормит сам; не призван — не выполнено.</summary>
public class PetHungryConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check() => new PetHungryCondition().Check(ClickerScript.Context(_world.Snapshot()));

    [Fact]
    public void Hungry()
    {
        _world.SetPet(1, hunger: 1);

        Assert.Equal("пет голоден", Check());
    }

    [Fact]
    public void FedIsNotHungry()
    {
        _world.SetPet(1, hunger: 0);

        Assert.Null(Check());
    }

    [Fact]
    public void NoPetOrNotSummonedIsNotHungry()
    {
        Assert.Null(Check());

        _world.SetPet(1, hunger: 2, summoned: false);
        Assert.Null(Check());
    }

    [Fact]
    public void LooksAtSummonedCage()
    {
        // Голоден пет в другой клетке, а призван сытый — кормить некого
        _world.SetPets(2, new PetInCage(1, 1f, 2), new PetInCage(2, 1f, 0));
        Assert.Null(Check());

        _world.SetPets(1, new PetInCage(1, 1f, 2), new PetInCage(2, 1f, 0));
        Assert.Equal("пет голоден", Check());
    }

    [Fact]
    public void Described() => Assert.Equal("пет голоден", new PetHungryCondition().Describe());
}

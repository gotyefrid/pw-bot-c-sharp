using Notes.Core.Clicker;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>Условие «HP пета меньше, %»: смотрит на призванного пета; не призван — не выполнено.</summary>
public class PetHpBelowConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(int percent) => new PetHpBelowCondition { Percent = percent }.Check(ClickerScript.Context(_world.Snapshot()));

    [Fact]
    public void HoldsBelowThreshold()
    {
        _world.SetPet(1, hpRatio: 0.25f);

        Assert.Equal("HP пета 25 % — меньше 50 %", Check(50));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void NotAtOrAboveThreshold(float hpRatio)
    {
        _world.SetPet(1, hpRatio);

        Assert.Null(Check(50));
    }

    [Fact]
    public void NoPetOrNotSummonedIsNotLowHp()
    {
        Assert.Null(Check(50));

        _world.SetPet(1, hpRatio: 0.1f, summoned: false);
        Assert.Null(Check(50));
    }

    [Fact]
    public void LooksAtSummonedCage()
    {
        // В другой клетке пет ранен, а призван здоровый — шаг не нужен
        _world.SetPets(2, new PetInCage(1, 0.1f, 0), new PetInCage(2, 0.9f, 0));
        Assert.Null(Check(50));

        _world.SetPets(1, new PetInCage(1, 0.1f, 0), new PetInCage(2, 0.9f, 0));
        Assert.Equal("HP пета 10 % — меньше 50 %", Check(50));
    }

    [Theory]
    [InlineData(40, 40)]
    [InlineData(500, 100)]
    [InlineData(0, 1)]
    public void FileKeepsPercentWithinLimits(int written, int read)
        => Assert.Equal(read, ClickerScript.RoundTrip(new PetHpBelowCondition { Percent = written }).Percent);

    [Fact]
    public void Described() => Assert.Equal("HP пета меньше 40 %", new PetHpBelowCondition { Percent = 40 }.Describe());
}

using BotCH.Core.Clicker;
using BotCH.Core.Logging;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>
/// Действие «Кормить пета»: выбранным кормом (пусто — самым простым), без подмены другим; голод не смотрит — это условие.
/// Корм на перезарядке — ничего не шлёт. Без галочки «ждать» — отправило и дальше, с ней — ждёт, пока стопка уменьшится.
/// </summary>
public class FeedPetActionTests
{
    private readonly ClickerScript _script = new();

    public FeedPetActionTests()
    {
        _script.World.SetPet(1, hunger: 1);
        _script.World.AddFood(3, 100, 5, loyalty: 10, "Мясо");
        _script.World.AddFood(4, 101, 5, loyalty: 5, "Трава");
    }

    private string FirstTurn(FeedPetAction action) => _script.Step(action).Run(_script.Start(), 1)[0];

    // Шаг кормления и за ним «F1»: что ушло в игру на первом снимке
    private string[] WithKey(FeedPetAction action) => [.. _script.Step(action).Key("F1").Run(_script.Start(), 1)];

    [Fact]
    public void ChosenFood() => Assert.Equal("use 3", FirstTurn(new FeedPetAction { Food = "мясо" }));

    [Fact]
    public void NothingChosenIsSimplest() => Assert.Equal("use 4", FirstTurn(new FeedPetAction()));

    [Fact]
    public void ChosenMissingIsNotReplaced()
    {
        Assert.Equal(["key F1"], WithKey(new FeedPetAction { Food = "Рыба" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("корма «Рыба» нет в сумке"));
    }

    [Fact]
    public void NoFoodIsReported()
    {
        _script.World.Bag.Clear();

        Assert.Equal(["key F1"], WithKey(new FeedPetAction()));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("нет корма в сумке"));
    }

    [Fact]
    public void EmptyStackIsNoFood()
    {
        _script.World.Bag.Clear();
        _script.World.AddFood(3, 100, 0, loyalty: 10, "Мясо");

        Assert.Equal(["key F1"], WithKey(new FeedPetAction { Food = "Мясо" }));
    }

    [Fact]
    public void PetNotSummonedIsReported()
    {
        _script.World.SetPet(1, summoned: false);

        Assert.Equal(["key F1"], WithKey(new FeedPetAction()));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("пет не призван"));
    }

    [Fact]
    public void CooldownSendsNothing()
    {
        _script.World.PetFoodCooldownMs = 45_000;

        Assert.Equal(["key F1"], WithKey(new FeedPetAction()));
        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Debug && e.Message.Contains("корм на перезарядке ещё 45 с"));
    }

    [Fact]
    public void DoesNotLookAtHunger()
    {
        // Голоден ли — решает условие «Пет голоден»
        _script.World.SetPet(1, hunger: 0);

        Assert.Equal("use 4", FirstTurn(new FeedPetAction()));
    }

    [Fact]
    public void DoesNotWaitByDefault() => Assert.Equal(["use 4, key F1"], WithKey(new FeedPetAction()));

    [Fact]
    public void WaitsForStackWhenAsked()
    {
        var bot = _script.Step(new FeedPetAction { WaitConfirmed = true }).Key("F1").Start();

        Assert.Equal(["use 4", ""], _script.Run(bot, 2));

        // Пет съел — стопка уменьшилась, корм на перезарядке (новый круг кормить не станет)
        _script.World.Consume(4);
        _script.World.PetFoodCooldownMs = 52_000;
        Assert.Equal(["key F1"], _script.Run(bot, 1));
    }

    [Fact]
    public void FileKeepsFoodTrimmed() => Assert.Equal("Мясо", ClickerScript.RoundTrip(new FeedPetAction { Food = " Мясо " }).Food);

    [Fact]
    public void Described()
    {
        Assert.Equal("кормить пета любым кормом", new FeedPetAction().Describe());
        Assert.Equal("кормить пета: Мясо", new FeedPetAction { Food = "Мясо" }.Describe());
    }
}

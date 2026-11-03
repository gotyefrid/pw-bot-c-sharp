using Notes.Core.Settings;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.Settings;

/// <summary>Сохранённые точки: у центра фарма и у места для смертей — свои списки.</summary>
public class FarmPointTests
{
    private readonly BotSettings _s = new();

    private FarmPoint Farm(string prefix = "Точка") => BotSettings.AddPoint(_s.Target.FarmPoints, prefix, new Position());
    private FarmPoint Death(string prefix = "Смерть") => BotSettings.AddPoint(_s.ExpDrain.DeathPoints, prefix, new Position());

    [Fact]
    public void NewPointTakesFirstFreeNumberInItsList()
    {
        Death();
        _s.ExpDrain.DeathPoints.Add(FarmPoint.At("смерть 3", new Position()));
        Farm("Смерть");

        Assert.Equal("Смерть 2", Death().Name);
        Assert.Equal("Смерть 4", Death().Name);
        Assert.Equal("Смерть 2", Farm("Смерть").Name);
    }

    [Fact]
    public void ListsDoNotMix()
    {
        Farm();
        Death();

        Assert.Equal(new[] { "Точка 1" }, _s.Target.FarmPoints.ConvertAll(p => p.Name));
        Assert.Equal(new[] { "Смерть 1" }, _s.ExpDrain.DeathPoints.ConvertAll(p => p.Name));
    }

    [Fact]
    public void SameNameAllowedInOtherList()
    {
        var farm = Farm();
        var death = Death();
        farm.Name = "Поляна";

        Assert.Null(BotSettings.WhyNotName(_s.ExpDrain.DeathPoints, death, "Поляна", "На месте фарма"));
    }

    [Fact]
    public void NameMustBeNewInItsListAndNotEmpty()
    {
        var a = Farm();
        Farm();

        Assert.Null(BotSettings.WhyNotName(_s.Target.FarmPoints, a, "Точка 1", "Точка старта"));
        Assert.Null(BotSettings.WhyNotName(_s.Target.FarmPoints, a, "Поляна", "Точка старта"));
        Assert.NotNull(BotSettings.WhyNotName(_s.Target.FarmPoints, a, "  ", "Точка старта"));
        Assert.NotNull(BotSettings.WhyNotName(_s.Target.FarmPoints, a, "точка 2", "Точка старта"));
        Assert.NotNull(BotSettings.WhyNotName(_s.Target.FarmPoints, a, "точка старта", "Точка старта"));
    }

    [Fact]
    public void RenameKeepsChoiceOnlyInItsList()
    {
        var farm = Farm();
        var death = Death();
        farm.Name = death.Name = "Поляна";
        _s.Target.FarmCenter = "Поляна";
        _s.ExpDrain.DeathPoint = "Поляна";

        _s.RenamePoint(death, "  У города ");

        Assert.Equal("У города", death.Name);
        Assert.Same(death, _s.ExpDrain.FindDeathPoint());
        Assert.Same(farm, _s.Target.SelectedFarmPoint);
        Assert.Equal("Поляна", farm.Name);
    }

    [Fact]
    public void RemoveClearsChoiceOnlyInItsList()
    {
        var farm = Farm();
        var death = Death();
        farm.Name = death.Name = "Поляна";
        _s.Target.FarmCenter = "Поляна";
        _s.ExpDrain.DeathPoint = "Поляна";

        _s.RemovePoint(farm);

        Assert.Empty(_s.Target.FarmPoints);
        Assert.Equal("", _s.Target.FarmCenter);
        Assert.Same(death, _s.ExpDrain.FindDeathPoint());
    }

    // Раньше точка для смертей была среди точек фарма
    [Fact]
    public void OldDeathPointMovesToItsList()
    {
        _s.Target.FarmPoints = [FarmPoint.At("Поляна", new Position(1, 2, 3)), FarmPoint.At("Смерть 1", new Position(4, 5, 6))];
        _s.Target.FarmCenter = "Поляна";
        _s.ExpDrain.DeathPoint = "Смерть 1";

        _s.Normalize();

        Assert.Equal(new[] { "Поляна" }, _s.Target.FarmPoints.ConvertAll(p => p.Name));
        Assert.Equal(new Position(4, 5, 6), _s.ExpDrain.FindDeathPoint()?.Position);
    }

    [Fact]
    public void OldDeathPointStaysAtFarmIfItIsFarmCenter()
    {
        _s.Target.FarmPoints = [FarmPoint.At("Поляна", new Position(1, 2, 3))];
        _s.Target.FarmCenter = "Поляна";
        _s.ExpDrain.DeathPoint = "Поляна";

        _s.Normalize();

        Assert.NotNull(_s.Target.SelectedFarmPoint);
        Assert.Equal(new Position(1, 2, 3), _s.ExpDrain.FindDeathPoint()?.Position);
        Assert.NotSame(_s.Target.SelectedFarmPoint, _s.ExpDrain.FindDeathPoint());
    }
}

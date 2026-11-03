using Notes.Core.Settings;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.Settings;

/// <summary>Сохранённые точки — одни на центр фарма и место для смертей.</summary>
public class FarmPointTests
{
    private readonly BotSettings _s = new();

    [Fact]
    public void NewPointTakesFirstFreeNumber()
    {
        _s.AddPoint("Смерть", new Position(1, 0, 0));
        _s.Target.FarmPoints.Add(FarmPoint.At("смерть 3", new Position()));

        Assert.Equal("Смерть 2", _s.AddPoint("Смерть", new Position()).Name);
        Assert.Equal("Смерть 4", _s.AddPoint("Смерть", new Position()).Name);
    }

    [Fact]
    public void RenameKeepsBothChoices()
    {
        var point = _s.AddPoint("Точка", new Position());
        _s.Target.FarmCenter = point.Name;
        _s.ExpDrain.DeathPoint = point.Name;

        _s.RenamePoint(point, "  Поляна ");

        Assert.Equal("Поляна", point.Name);
        Assert.Same(point, _s.Target.SelectedFarmPoint);
        Assert.Same(point, _s.ExpDrain.FindDeathPoint(_s.Target));
    }

    [Fact]
    public void RenameDoesNotTouchOtherChoice()
    {
        var farm = _s.AddPoint("Точка", new Position());
        var death = _s.AddPoint("Смерть", new Position());
        _s.Target.FarmCenter = farm.Name;
        _s.ExpDrain.DeathPoint = death.Name;

        _s.RenamePoint(death, "У города");

        Assert.Equal("Точка 1", _s.Target.FarmCenter);
        Assert.Equal("У города", _s.ExpDrain.DeathPoint);
    }

    [Fact]
    public void NameMustBeNewAndNotEmpty()
    {
        var a = _s.AddPoint("Точка", new Position());
        _s.AddPoint("Точка", new Position());

        Assert.Null(_s.WhyNotName(a, "Точка 1", "Точка старта"));
        Assert.Null(_s.WhyNotName(a, "Поляна", "Точка старта"));
        Assert.NotNull(_s.WhyNotName(a, "  ", "Точка старта"));
        Assert.NotNull(_s.WhyNotName(a, "точка 2", "Точка старта"));
        Assert.NotNull(_s.WhyNotName(a, "точка старта", "Точка старта"));
    }

    [Fact]
    public void RemovedPointClearsWhereChosen()
    {
        var point = _s.AddPoint("Точка", new Position());
        _s.Target.FarmCenter = point.Name;
        _s.ExpDrain.DeathPoint = point.Name;

        _s.RemovePoint(point);

        Assert.Empty(_s.Target.FarmPoints);
        Assert.Equal("", _s.Target.FarmCenter);
        Assert.Null(_s.ExpDrain.FindDeathPoint(_s.Target));
        Assert.Equal("", _s.ExpDrain.DeathPoint);
    }
}

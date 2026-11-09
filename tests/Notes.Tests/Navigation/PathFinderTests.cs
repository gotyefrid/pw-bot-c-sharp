using System;
using System.Linq;
using Notes.Core.Navigation;
using Xunit;

namespace Notes.Tests.Navigation;

public class PathFinderTests
{
    // Поле из функции «занято ли»: персонаж — точка, края препятствий — с запасом на его круг заданы в самой функции
    private sealed class Area(Func<float, float, bool> blocked) : IWalkArea
    {
        public bool IsFree(float x, float y) => !blocked(x, y);
    }

    private static PathFinder Finder(Func<float, float, bool> blocked) => new(new Area(blocked));

    // Каждый отрезок пути свободен
    private static void AssertWalkable(PathFinder finder, PathResult path)
    {
        Assert.True(path.Found, path.Problem);
        for (var i = 1; i < path.Points.Count; i++)
            Assert.True(finder.LineFree(path.Points[i - 1], path.Points[i]), $"отрезок {path.Points[i - 1]} → {path.Points[i]} занят");
    }

    [Fact]
    public void GoesStraightWhenNothingInTheWay()
    {
        var path = Finder((_, _) => false).Find(new(0, 0), new(30, 10));

        Assert.Equal([new PathPoint(0, 0), new PathPoint(30, 10)], path.Points);
        Assert.Equal(0, path.Explored);
    }

    [Fact]
    public void WalksAroundRockInTheMiddle()
    {
        // Камень 6×6 м посередине прямой
        var finder = Finder((x, y) => Math.Abs(x - 10) < 3 && Math.Abs(y) < 3);

        var path = finder.Find(new(0, 0), new(20, 0));

        AssertWalkable(finder, path);
        Assert.InRange(path.Points.Count, 3, 5);
        Assert.InRange(path.Length, 20.5f, 24f);
    }

    [Fact]
    public void GoesThroughGapWideEnough()
    {
        // Стена x = 10 с дыркой y ∈ (4, 6)
        var finder = Finder((x, y) => Math.Abs(x - 10) < 0.5f && !(y > 4 && y < 6));

        var path = finder.Find(new(0, 0), new(20, 0));

        AssertWalkable(finder, path);
        Assert.Contains(path.Points, p => p.Y > 3 && p.Y < 7 && Math.Abs(p.X - 10) < 3);
    }

    [Fact]
    public void KeepsMarginFromCornersAndFallsBackToTightGap()
    {
        // Угол дома: квадрат 0…10 по обе оси; «с запасом» — тот же дом, раздутый на 0,3 м
        bool House(float x, float y, float grow) => x > -grow && x < 10 + grow && y > -grow && y < 10 + grow;
        var tight = new Area((x, y) => House(x, y, 0));
        var wide = new Area((x, y) => House(x, y, 0.3f));

        var path = PathFinder.FindWithMargin(wide, tight, new(-2, 5), new(5, 12));

        Assert.True(path.Found, path.Problem);
        Assert.False(path.Tight);
        Assert.All(path.Points, p => Assert.True(wide.IsFree(p.X, p.Y)));

        // Щель шириной 0,4 м: с запасом закрыта, впритирку — проходима
        bool Wall(float x, float y, float grow) => Math.Abs(x - 10) < 0.5f && !(y > 5 - 0.2f + grow && y < 5 + 0.2f - grow);
        var narrow = PathFinder.FindWithMargin(new Area((x, y) => Wall(x, y, 0.3f) || Math.Abs(y - 5) > 30),
            new Area((x, y) => Wall(x, y, 0) || Math.Abs(y - 5) > 30), new(0, 5), new(20, 5));

        Assert.True(narrow.Found, narrow.Problem);
        Assert.True(narrow.Tight);
    }

    [Fact]
    public void ExplainsWhenTargetIsCutOff()
    {
        // Цель внутри кольца стен
        var finder = Finder((x, y) =>
        {
            var r = Math.Sqrt((x - 20) * (x - 20) + y * y);
            return r is > 4 and < 6;
        });

        var path = finder.Find(new(0, 0), new(20, 0));

        Assert.False(path.Found);
        Assert.Contains("обхода нет", path.Problem);
        Assert.Empty(path.Points);
    }

    [Fact]
    public void StepsAwayFromWallWhenStartingInsideIt()
    {
        // Перс прижат к забору: его точка «занята»; забор x ∈ (−1, 1) до y = 10
        var finder = Finder((x, y) => Math.Abs(x) < 1 && y < 10);

        var path = finder.Find(new(0.5f, 0), new(-10, 0));

        Assert.True(path.Found, path.Problem);
        Assert.Equal(new PathPoint(0.5f, 0), path.Points[0]);
        Assert.True(new Area((x, y) => Math.Abs(x) < 1 && y < 10).IsFree(path.Points[1].X, path.Points[1].Y));
        Assert.Equal(new PathPoint(-10, 0), path.Points.Last());
    }

    [Fact]
    public void RefusesTooFarTarget()
    {
        var path = Finder((_, _) => false).Find(new(0, 0), new(1000, 0));

        Assert.False(path.Found);
        Assert.Contains("дальше", path.Problem);
    }
}

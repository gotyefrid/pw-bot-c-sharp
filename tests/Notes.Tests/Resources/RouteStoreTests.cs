using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Notes.Core.Resources;
using Notes.Core.Settings;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.Resources;

/// <summary>Общий файл маршрутов: видят все копии бота, правка одного маршрута не затирает другие, удалённый не возвращается.</summary>
public class RouteStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "notes-routes-" + Guid.NewGuid().ToString("N"));

    public RouteStoreTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File => Path.Combine(_folder, RouteStore.FileName);

    // Две копии бота — два своих объекта на один файл
    private RouteStore Store() => new(File);

    private static List<RoutePoint> Points(params float[] xs)
        => xs.Select((x, i) => RoutePoint.At($"Точка {i + 1}", new Position(x, 30, 0))).ToList();

    [Fact]
    public void NewRoutesGetNumberedNames()
    {
        var store = Store();

        store.Create();
        store.Create();

        Assert.Equal(["Маршрут 1", "Маршрут 2"], store.All().Select(r => r.Name));
    }

    [Fact]
    public void PointsSurviveReadingBackWithWhatToDig()
    {
        var store = Store();
        var route = store.Create();
        var points = Points(10, 20);
        points[1].ListMode = LootListMode.OnlyListed;
        points[1].Resources = ["Чёрное железо"];

        Assert.True(store.SavePoints(route.Id, points));

        var read = Store().Find(route.Id)!;
        Assert.Equal([10f, 20f], read.Points.Select(p => p.X));
        Assert.Equal(LootListMode.OnlyListed, read.Points[1].ListMode);
        Assert.Equal(["Чёрное железо"], read.Points[1].Resources);
        // Копия: правка своих точек файл не меняет
        points[0].X = 99;
        Assert.Equal(10f, Store().Find(route.Id)!.Points[0].X);
    }

    [Fact]
    public void EditOfOneRouteKeepsRouteAddedByAnotherCopy()
    {
        var mine = Store();
        var other = Store();
        var a = mine.Create();
        var b = other.Create();

        mine.SavePoints(a.Id, Points(10));

        Assert.Equal(2, other.All().Count);
        Assert.Single(other.Find(a.Id)!.Points);
        Assert.NotNull(mine.Find(b.Id));
    }

    [Fact]
    public void RenameKeepsIdAndRefusesTakenOrEmptyName()
    {
        var store = Store();
        var a = store.Create();
        var b = store.Create();

        Assert.True(store.Rename(a.Id, "  Железо у Города  "));
        Assert.False(store.Rename(b.Id, "железо у города"));
        Assert.False(store.Rename(b.Id, "  "));

        Assert.Equal("Железо у Города", store.Find(a.Id)!.Name);
        Assert.Equal("Маршрут 2", store.Find(b.Id)!.Name);
    }

    [Fact]
    public void OldCharacterPointsBecomeSharedRouteOnce()
    {
        // Точки от прошлой версии у двух персонажей — одинаковые: общий маршрут один, выбран у обоих
        var store = Store();
        var first = new RouteSettings { Points = Points(10, 20) };
        var second = new RouteSettings { Points = Points(10, 20) };

        Assert.Equal(RouteSync.Imported, store.Sync(first, "Маршрут SaudiKo"));
        Assert.Equal(RouteSync.Imported, store.Sync(second, "Маршрут Lamabuka"));

        Assert.Equal(["Маршрут SaudiKo"], store.All().Select(r => r.Name));
        Assert.Equal(first.RouteId, second.RouteId);
        Assert.Equal(RouteSync.Unchanged, store.Sync(first, "Маршрут SaudiKo"));
    }

    [Fact]
    public void EmptyCharacterWithoutRouteStaysWithout()
    {
        var route = new RouteSettings();

        Assert.Equal(RouteSync.Unchanged, Store().Sync(route, "Маршрут SaudiKo"));
        Assert.Equal("", route.RouteId);
        Assert.False(System.IO.File.Exists(File));
    }

    [Fact]
    public void PointsChangedByAnotherCopyAreTaken()
    {
        var store = Store();
        var saved = store.Create();
        var mine = new RouteSettings { RouteId = saved.Id };

        Store().SavePoints(saved.Id, Points(10, 20, 30));

        Assert.Equal(RouteSync.Loaded, store.Sync(mine, "Маршрут SaudiKo"));
        Assert.Equal([10f, 20f, 30f], mine.Points.Select(p => p.X));
    }

    [Fact]
    public void DeletedRouteLeavesCharacterWithoutRouteAndDoesNotComeBack()
    {
        var store = Store();
        var saved = store.Create();
        store.SavePoints(saved.Id, Points(10));
        var mine = new RouteSettings { RouteId = saved.Id, Points = Points(10) };

        Assert.True(Store().Remove(saved.Id));

        Assert.Equal(RouteSync.Deleted, store.Sync(mine, "Маршрут SaudiKo"));
        Assert.Equal("", mine.RouteId);
        Assert.Empty(mine.Points);
        Assert.Empty(store.All());
        Assert.False(store.SavePoints(saved.Id, Points(20)));
    }

    [Fact]
    public void LostFileGetsRouteBackFromCharacterPoints()
    {
        // Папку %AppData% почистили — маршрут не удаляли: возвращается в файл с тем же номером
        var mine = new RouteSettings { RouteId = "abc", Points = Points(10, 20) };

        Assert.Equal(RouteSync.Imported, Store().Sync(mine, "Маршрут SaudiKo"));

        var back = Store().Find("abc")!;
        Assert.Equal("Маршрут SaudiKo", back.Name);
        Assert.Equal(2, back.Points.Count);
    }

    [Fact]
    public void BrokenFileIsKeptAsBadAndStoreStartsEmpty()
    {
        System.IO.File.WriteAllText(File, "{ это не json");
        var store = Store();

        Assert.Empty(store.All());
        Assert.NotNull(store.Problem);
        Assert.True(System.IO.File.Exists(File + ".bad"));
    }
}

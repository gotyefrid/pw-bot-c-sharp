using System;
using System.IO;
using System.Threading;
using Notes.Core.Navigation;
using Notes.Core.Profiles;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.Navigation;

public class ObstacleMapTests
{
    // Тест на файлах настоящего клиента: папка element — в переменной окружения. Нет переменной — тест пропускается
    private const string Variable = "NOTES_COMEBACK136_DIR";
    private static readonly string Directory = Environment.GetEnvironmentVariable(Variable) ?? "";

    [Fact]
    public void TileBoundsMatchTileNumbers()
    {
        // Мир 8×11: квадрат 1 — северо-западный, 21 — третья строка, пятый столбец (там двор, где проверяли обход)
        Assert.Equal((-4096f, 4608f, -3072f, 5632f), ObstacleMap.TileBounds(1, 11, 8));
        Assert.Equal((0f, 2560f, 1024f, 3584f), ObstacleMap.TileBounds(21, 11, 8));
    }

    [Fact]
    public void TilesAroundTakeNeighboursNearEdge()
    {
        // У самого края квадрата 21 (x = 1000) — задеваем и соседний справа (22)
        Assert.Equal([21], ObstacleMap.TilesAround(500, 3000, 240, 11, 8));
        Assert.Contains(22, ObstacleMap.TilesAround(1000, 3000, 240, 11, 8));
    }

    [Fact]
    public void RealClientYardIsLoadedInBackground()
    {
        Assert.SkipUnless(System.IO.Directory.Exists(Directory), $"Не задана {Variable} — папка element клиента Comeback 1.3.6");

        var format = new ProfileCatalog().Load("comeback136").Data.GameFiles.Pck;
        var map = new ObstacleMap(Directory, "world", format);
        // Угол двора за забором (проверено 09.11): сначала квадраты читаются — ответа нет, потом есть
        var corner = new Position(960, 225.2f, 3168);
        Assert.Null(map.Around(corner));

        (IWalkArea Tight, IWalkArea Comfort)? area = null;
        for (var i = 0; i < 100 && area is null; i++)
        {
            Thread.Sleep(100);
            area = map.Around(corner);
        }

        Assert.NotNull(area);
        // Дом у выхода со двора (952,8…960,6; 3191,8…3201,1) — занят; середина двора — свободна
        Assert.False(area.Value.Tight.IsFree(956.5f, 3196.5f));
        Assert.True(area.Value.Tight.IsFree(964.5f, 3177));
        Assert.True(new PathFinder(area.Value.Tight).Find(new(960, 3168), new(930, 3190)).Found);
    }
}

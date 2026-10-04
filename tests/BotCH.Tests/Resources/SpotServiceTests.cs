using System;
using System.IO;
using System.Linq;
using BotCH.Core.Logging;
using BotCH.Core.Resources;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Resources;

/// <summary>Блокнот точек с файлами: сохраняет раз в 10 с, подтягивает точки других копий бота, переносит старый файл.</summary>
public class SpotServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "botch-spots-" + Guid.NewGuid().ToString("N"));
    private readonly FakeWorld _world = new();
    private DateTime _now = new(2026, 10, 4, 12, 0, 0);

    public SpotServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string SharedFile => Path.Combine(_folder, "resources.json");

    private SpotService Service() => new(_folder, NullLogger.Instance, () => _now);

    private void SeeOre(string name = "Железная руда", float x = 10)
        => _world.Ground.Add(new GroundItem(0, 0xC0000001, 3079, GroundItemKind.Resource, new Position(x, 0, 0), name));

    private static string[] Names(string file) => new SpotBookStore(file).Load(out _).Select(s => s.Name).ToArray();

    [Fact]
    public void NewSpotIsSavedOnlyAfterTenSeconds()
    {
        var spots = Service();
        SeeOre();

        spots.Observe(_world.Snapshot());
        _now += TimeSpan.FromSeconds(5);
        spots.Observe(_world.Snapshot());
        Assert.False(File.Exists(SharedFile));

        _now += TimeSpan.FromSeconds(5);
        spots.Observe(_world.Snapshot());

        Assert.Equal(["Железная руда"], Names(SharedFile));
    }

    [Fact]
    public void SpotsOfAnotherBotCopyArePulledInEveryMinute()
    {
        var spots = Service();
        new SpotBookStore(SharedFile).Save([new ResourceSpot { Name = "Залежи камня", X = 500, Y = 500 }]);

        _now += TimeSpan.FromSeconds(30);
        spots.Observe(_world.Snapshot());
        Assert.Empty(spots.Spots);

        _now += TimeSpan.FromSeconds(30);
        spots.Observe(_world.Snapshot());

        Assert.Equal(["Залежи камня"], spots.Spots.Select(s => s.Name));
    }

    [Fact]
    public void SaveMergesWithFileInsteadOfOverwritingIt()
    {
        // Другая копия успела записать свою точку — при нашем сохранении она не пропадает
        var spots = Service();
        SeeOre();
        spots.Observe(_world.Snapshot());
        new SpotBookStore(SharedFile).Save([new ResourceSpot { Name = "Залежи камня", X = 500, Y = 500 }]);

        spots.Save();

        Assert.Equal(["Железная руда", "Залежи камня"], Names(SharedFile).OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void OldFileFromBotFolderIsMovedIntoSharedFile()
    {
        var old = Path.Combine(_folder, "old-resources.json");
        new SpotBookStore(old).Save([new ResourceSpot { Name = "Высохший древесный корень", X = 1, Y = 2 }]);

        Service().ImportOld(old);

        Assert.Equal(["Высохший древесный корень"], Names(SharedFile));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(old + ".imported"));
    }
}

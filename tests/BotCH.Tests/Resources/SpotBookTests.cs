using System;
using System.IO;
using System.Linq;
using BotCH.Core.Resources;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Resources;

public class SpotBookTests : IDisposable
{
    private readonly FakeWorld _world = new();
    private readonly SpotBook _book = new([]) { Server = "comeback146" };
    private readonly string _file = Path.Combine(Path.GetTempPath(), "botch-spots-" + Guid.NewGuid().ToString("N") + ".json");
    private uint _nextId = 0xC0100E80;

    public void Dispose()
    {
        foreach (var f in new[] { _file, _file + ".bad", _file + ".tmp" })
            if (File.Exists(f))
                File.Delete(f);
    }

    private GroundItem Resource(string name, float x, float y = 0)
    {
        var item = new GroundItem(1, _nextId++, 3079, GroundItemKind.Resource, new Position(x, 0, y), 0, name);
        _world.Ground.Add(item);
        return item;
    }

    private void Observe(double afterSeconds = 0.25)
    {
        _world.Wait(afterSeconds);
        _book.Observe(_world.Snapshot());
    }

    [Fact]
    public void SeenResourceBecomesSpotAndSameNameNearbyIsSameSpot()
    {
        Resource("Высохший древесный корень", 10);
        Observe();
        Assert.Single(_book.Spots);

        // Выкопали и появился в 6 м от прежнего места — та же точка, центр сдвинулся
        _world.Ground.Clear();
        _world.Position = new Position(200, 0, 0);
        for (var i = 0; i < 3; i++)
            Observe();
        _world.Position = new Position(0, 0, 0);
        Observe();
        Resource("Высохший древесный корень", 16);
        Observe();

        var spot = Assert.Single(_book.Spots);
        Assert.Equal(2, spot.Seen);
        Assert.Equal(13, spot.X, 1);
        Assert.Equal(3, spot.Spread, 1);
    }

    [Fact]
    public void SameResourceIdFartherThanMergeRadiusIsSameSpot()
    {
        // Как в игре: корень 0xC0100E80 выкопан и через 10 мин появился в 18–25 м — с тем же номером
        var root = Resource("Высохший древесный корень", 10);
        Observe();
        _world.Ground.Clear();
        for (var i = 0; i < 12; i++)
            Observe();

        _world.Ground.Add(root with { Position = new Position(35, 0, 0) });
        Observe();

        var spot = Assert.Single(_book.Spots);
        Assert.Equal(root.Id, spot.Ids["comeback146"]);
        Assert.Equal(2, spot.Seen);
        Assert.Null(spot.GoneAt);

        // Тот же номер, но в 200 м — другое место
        _world.Ground.Clear();
        _world.Ground.Add(root with { Position = new Position(200, 0, 0) });
        _world.Position = new Position(190, 0, 0);
        for (var x = 20; x <= 190; x += 20)
        {
            _world.Position = new Position(x, 0, 0);
            Observe();
        }
        Assert.Equal(2, _book.Spots.Count);
    }

    [Fact]
    public void ResourceIdsAreKeptPerServer()
    {
        var root = Resource("Высохший древесный корень", 10);
        Observe();

        // Тот же корень на другом сервере — другой номер; номер прежнего сервера не теряется
        _book.Server = "pwclassic136";
        _book.Forget();
        _world.Ground.Clear();
        _world.Ground.Add(root with { Id = 0xC0100AE4, Position = new Position(15, 0, 0) });
        Observe();

        var spot = Assert.Single(_book.Spots);
        Assert.Equal(root.Id, spot.Ids["comeback146"]);
        Assert.Equal(0xC0100AE4u, spot.Ids["pwclassic136"]);

        // Номер с 1.4.6 на 1.3.6 ничего не значит: тот же номер в 40 м — не эта точка
        _book.Forget();
        _world.Ground.Clear();
        _world.Ground.Add(root with { Position = new Position(-30, 0, 0) });
        Observe();
        Assert.Equal(2, _book.Spots.Count);
    }

    [Fact]
    public void ManyPointsOfOneNameAndDifferentNamesAtOnePlaceAreSeparate()
    {
        Resource("Высохший древесный корень", 10);
        Resource("Высохший древесный корень", 60);
        Resource("Шалфей", 12);
        Observe();

        Assert.Equal(3, _book.Spots.Count);
        Assert.Equal(2, _book.Spots.Count(s => s.Name == "Высохший древесный корень"));
    }

    [Fact]
    public void ResourceOutOfSightIsNotDug()
    {
        var sage = Resource("Шалфей", 10);
        Observe();

        // Уходим шагами по 20 м; шалфей пропал из списка, когда до него стало 95 м — просто не видно
        for (var x = -20; x >= -80; x -= 20)
        {
            _world.Position = new Position(x, 0, 0);
            Observe();
        }
        _world.Position = new Position(-85, 0, 0);
        _world.Ground.Remove(sage);
        for (var i = 0; i < 12; i++)
            Observe();

        var spot = Assert.Single(_book.Spots);
        Assert.Null(spot.GoneAt);
        Assert.False(_book.IsPresent(spot));
    }

    [Fact]
    public void ResourceGoneNearbyIsDug()
    {
        Resource("Молочай", 40);
        Observe();
        _book.MarkSaved();

        _world.Ground.Clear();
        Observe();
        var gone = _world.Time;
        Observe(1);
        Assert.Null(_book.Spots[0].GoneAt);
        Observe(1.5);
        Assert.Equal(gone, _book.Spots[0].GoneAt);
        Assert.True(_book.Changed);
    }

    [Fact]
    public void ListBlinkOrTeleportIsNotDigging()
    {
        Resource("Шалфей", 10);
        Observe();

        // Мигнул список на секунду
        var item = _world.Ground[0];
        _world.Ground.Clear();
        Observe(1);
        _world.Ground.Add(item);
        Observe(1);
        Assert.Null(_book.Spots[0].GoneAt);

        // Телепорт рядом с точкой: всё, что было видно, забыто — пропажа не засчитывается
        _world.Ground.Clear();
        _world.Position = new Position(50, 0, 0);
        Observe();
        _world.Position = new Position(5, 0, 0);
        for (var i = 0; i < 12; i++)
            Observe();
        Assert.Null(_book.Spots[0].GoneAt);
    }

    [Fact]
    public void AppearedAgainClearsGone()
    {
        Resource("Шалфей", 10);
        Observe();
        _world.Ground.Clear();
        for (var i = 0; i < 12; i++)
            Observe();
        Assert.NotNull(_book.Spots[0].GoneAt);

        Resource("Шалфей", 11);
        Observe();
        Assert.Null(_book.Spots[0].GoneAt);
        Assert.True(_book.IsPresent(_book.Spots[0]));
    }

    [Fact]
    public void ManualSpotSnapsToRealResourceAndNoDuplicates()
    {
        var spot = _book.Add("Железная руда", new Position(0, 0, 0));
        Assert.True(spot.Manual);
        Assert.Same(spot, _book.Add("Железная руда", new Position(5, 0, 0)));

        Resource("Железная руда", 12);
        Observe();

        Assert.Single(_book.Spots);
        Assert.Equal(12, spot.X, 1);
        Assert.Equal(0, spot.Spread, 1);
    }

    [Fact]
    public void RemovedSpotIsGone()
    {
        var spot = _book.Add("Шалфей", new Position(0, 0, 0));
        _book.MarkSaved();

        Assert.True(_book.Remove(spot));
        Assert.Empty(_book.Spots);
        Assert.True(_book.Changed);
    }

    [Fact]
    public void StoreRoundTripAndBadFile()
    {
        var store = new SpotBookStore(_file);
        Assert.Empty(store.Load(out var none));
        Assert.Null(none);

        Resource("Шалфей", 10, 20);
        Observe();
        store.Save(_book.Spots);

        var loaded = Assert.Single(store.Load(out _));
        Assert.Equal("Шалфей", loaded.Name);
        Assert.Equal(new Position(10, 0, 20), loaded.Position);
        Assert.Equal(1, loaded.Seen);
        Assert.Equal(_world.Time, loaded.LastSeen);

        File.WriteAllText(_file, "{ испорчен");
        Assert.Empty(store.Load(out var problem));
        Assert.NotNull(problem);
        Assert.True(File.Exists(_file + ".bad"));
    }
}

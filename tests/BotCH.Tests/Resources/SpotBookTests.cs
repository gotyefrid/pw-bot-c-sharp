using System;
using System.Collections.Generic;
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
    private const string Server = "comeback146";
    private readonly SpotBook _book = new([]) { Server = Server };
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
    public void NonResourcesAreNotSpots()
    {
        var corpse = Resource("Безымянный труп", 5);
        _world.Ground[_world.Ground.IndexOf(corpse)] = corpse with { Special = true };
        Resource("Шалфей", 10);

        Observe();

        Assert.Equal(["Шалфей"], _book.Spots.Select(s => s.Name));
    }

    [Fact]
    public void SeenResourceBecomesSpotAndSameNameNearbyIsSameSpot()
    {
        var root = Resource("Высохший древесный корень", 10);
        Observe();
        Assert.Single(_book.Spots);

        // Выкопали и появился в 6 м от прежнего места — та же точка, центр сдвинулся
        _world.Ground.Clear();
        _world.Position = new Position(200, 0, 0);
        for (var i = 0; i < 3; i++)
            Observe();
        _world.Position = new Position(0, 0, 0);
        Observe();
        _world.Ground.Add(root with { Position = new Position(16, 0, 0) });
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
        Assert.Null(spot.GoneOn(Server));

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

        // Уходим шагами по 20 м; шалфей пропал из списка, когда до него стало 55 м — уже не наверняка видно
        for (var x = -10; x >= -40; x -= 15)
        {
            _world.Position = new Position(x, 0, 0);
            Observe();
        }
        _world.Position = new Position(-45, 0, 0);
        _world.Ground.Remove(sage);
        for (var i = 0; i < 12; i++)
            Observe();

        var spot = Assert.Single(_book.Spots);
        Assert.Null(spot.GoneOn(Server));
        Assert.False(_book.IsPresent(spot));
    }

    [Fact]
    public void ResourceGoneNearbyIsDug()
    {
        Resource("Молочай", 30);
        Observe();
        _book.MarkSaved();

        _world.Ground.Clear();
        Observe();
        var gone = _world.Time;
        Observe(1);
        Assert.Null(_book.Spots[0].GoneOn(Server));
        Observe(1.5);
        Assert.Equal(gone, _book.Spots[0].GoneOn(Server));
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
        Assert.Null(_book.Spots[0].GoneOn(Server));

        // Телепорт рядом с точкой: всё, что было видно, забыто — пропажа не засчитывается
        _world.Ground.Clear();
        _world.Position = new Position(50, 0, 0);
        Observe();
        _world.Position = new Position(5, 0, 0);
        for (var i = 0; i < 12; i++)
            Observe();
        Assert.Null(_book.Spots[0].GoneOn(Server));
    }

    [Fact]
    public void AppearedAgainClearsGone()
    {
        var sage = Resource("Шалфей", 10);
        Observe();
        _world.Ground.Clear();
        for (var i = 0; i < 12; i++)
            Observe();
        Assert.NotNull(_book.Spots[0].GoneOn(Server));

        _world.Ground.Add(sage with { Position = new Position(11, 0, 0) });
        Observe();
        Assert.Null(_book.Spots[0].GoneOn(Server));
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
    public void MergeAddsOtherCopiesSpotsAndKeepsFreshestState()
    {
        Resource("Шалфей", 10);
        Observe();
        var mine = _book.Spots[0];

        // Другая копия бота: тот же шалфей (выкопан позже, чем мы его видели) и новый корень
        var dug = _world.Time.AddMinutes(1);
        var other = new[]
        {
            new ResourceSpot { Name = "Шалфей", X = 14, Seen = 3, LastSeen = _world.Time.AddSeconds(30), Gone = { [Server] = dug }, Ids = { ["pwclassic136"] = 7 } },
            new ResourceSpot { Name = "Высохший древесный корень", X = 300, Seen = 1 },
        };
        _world.Ground.Clear();
        _book.Forget();

        Assert.Equal(1, _book.Merge(other));
        Assert.Equal(2, _book.Spots.Count);
        Assert.Equal(dug, mine.GoneOn(Server));
        Assert.Equal(3, mine.Seen);
        Assert.Equal(7u, mine.Ids["pwclassic136"]);

        // Повторное слияние ничего не дублирует; удалённая у нас точка из файла не возвращается
        Assert.Equal(0, _book.Merge(other));
        _book.Remove(_book.Spots.Single(s => s.X == 300));
        Assert.Equal(0, _book.Merge(other));
        Assert.Single(_book.Spots);
    }

    [Fact]
    public void EventsTellWhenAndWhereResourceCameBack()
    {
        var events = new List<SpotEvent>();
        _book.Happened += events.Add;

        var root = Resource("Высохший древесный корень", 10);
        Observe();
        _world.Ground.Clear();
        for (var i = 0; i < 12; i++)
            Observe();
        var dug = events.Last().Time;
        _world.Wait(600);
        _world.Ground.Add(root with { Position = new Position(28, 0, 0) });
        Observe();

        Assert.Equal([SpotEventKind.New, SpotEventKind.Dug, SpotEventKind.Respawned], events.Select(e => e.Kind));
        var back = events.Last();
        Assert.Equal(root.Id, back.ResourceId);
        Assert.Equal(18, back.FromCenter, 1);
        Assert.Equal(_world.Time - dug, back.SinceDug);
    }

    [Fact]
    public void EmptyWhenNearKnownSpotWithoutResourceAndNotSeenDug()
    {
        var events = new List<SpotEvent>();
        var book = new SpotBook([new ResourceSpot { Name = "Шалфей", X = 50, Seen = 2 }]) { Server = "comeback146" };
        book.Happened += events.Add;

        // Далеко (70 м) — молчим; подошли на 20 м к центру и постояли 3 с — «пусто», один раз
        _world.Position = new Position(-20, 0, 0);
        for (var i = 0; i < 20; i++)
            book.Observe(_world.Wait(0.25).Snapshot());
        Assert.Empty(events);
        for (var x = 0; x <= 30; x += 10)
        {
            _world.Position = new Position(x, 0, 0);
            book.Observe(_world.Wait(0.25).Snapshot());
        }
        for (var i = 0; i < 40; i++)
            book.Observe(_world.Wait(0.25).Snapshot());
        Assert.Equal([SpotEventKind.Empty], events.Select(e => e.Kind));

        // Ресурс появился, хоть копки мы и не видели — «в поле зрения»
        _world.Ground.Add(new GroundItem(1, 0xC0100B37, 3536, GroundItemKind.Resource, new Position(55, 0, 0), 0, "Шалфей"));
        book.Observe(_world.Wait(0.25).Snapshot());
        Assert.Equal(SpotEventKind.InView, events.Last().Kind);
        Assert.Equal(5, events.Last().FromCenter, 1);
    }

    [Fact]
    public void DugByUsWhenOurGatherBarWasOnThisResourceAndBagGained()
    {
        var events = new List<SpotEvent>();
        _book.Happened += events.Add;
        _world.Gather = new GatherProgress(false, 0, 0);
        _world.AddPotion(1, 3074, 2);

        var root = Resource("Высохший древесный корень", 3);
        var sage = Resource("Шалфей", 30);
        Observe();

        // Копаем корень 5 с; корень пропал, в сумке +1 (tid 3074)
        for (var ms = 0; ms <= 5000; ms += 250)
        {
            _world.Gather = new GatherProgress(true, ms, 5000);
            Observe();
        }
        _world.Gather = new GatherProgress(false, 0, 0);
        _world.Ground.Remove(root);
        _world.Bag[0] = _world.Bag[0] with { Count = 3 };
        for (var i = 0; i < 12; i++)
            Observe();

        // А шалфей пропал без нашей копки
        _world.Ground.Remove(sage);
        for (var i = 0; i < 12; i++)
            Observe();

        var dug = events.Where(e => e.Kind == SpotEventKind.Dug).ToList();
        Assert.Equal(2, dug.Count);
        Assert.Equal(("Высохший древесный корень", "мы", "3074×1"), (dug[0].Name, dug[0].Who, dug[0].Loot));
        Assert.Equal(5, dug[0].DigSeconds!.Value, 1);
        Assert.Equal(("Шалфей", "другой"), (dug[1].Name, dug[1].Who));
    }

    [Fact]
    public void ResourceRespawnsAnywhereInItsAreaWithSameId()
    {
        // Как в игре: тот же номер возродился в 90 м от прежнего места — тот же участок, не новая точка
        var root = Resource("Высохший древесный корень", 10);
        Observe();
        _world.Ground.Clear();
        for (var i = 0; i < 12; i++)
            Observe();

        _world.Ground.Add(root with { Position = new Position(-80, 0, 0) });
        Observe();

        var spot = Assert.Single(_book.Spots);
        Assert.Null(spot.GoneOn(Server));
        Assert.True(_book.IsPresent(spot));
    }

    [Fact]
    public void OtherIdNearbyIsNeighbourArea()
    {
        // Корень с другим номером в 30 м от точки, у которой на этом сервере свой номер, — соседний участок
        Resource("Высохший древесный корень", 10);
        Observe();
        Resource("Высохший древесный корень", 40);
        Observe();

        Assert.Equal(2, _book.Spots.Count);
    }

    [Fact]
    public void ResourceFarFromCenterIsWatchedWhereItIs()
    {
        // Точка — участок с центром в 0; ресурс сейчас в 50 м от центра, перс рядом с ним (в 60 м от центра):
        // ресурс пропал — это копка, а не «ушёл из виду»
        var spot = new ResourceSpot { Name = "Шалфей", Seen = 5, Ids = { [Server] = 0xC0100B37 } };
        var book = new SpotBook([spot]) { Server = Server };
        var sage = new GroundItem(1, 0xC0100B37, 3536, GroundItemKind.Resource, new Position(50, 0, 0), 0, "Шалфей");
        _world.Ground.Add(sage);
        _world.Position = new Position(60, 0, 0);
        book.Observe(_world.Wait(0.25).Snapshot());

        _world.Ground.Clear();
        for (var i = 0; i < 12; i++)
            book.Observe(_world.Wait(0.25).Snapshot());

        Assert.NotNull(spot.GoneOn(Server));
    }

    [Fact]
    public void DigOnOneServerMeansNothingOnAnother()
    {
        // Общий файл точек: копка на 1.3.6 не делает ресурс 1.4.6 «выкопанным»
        var dug = _world.Time.AddMinutes(-3);
        var spot = new ResourceSpot { Name = "Шалфей", Seen = 2, Gone = { ["pwclassic136"] = dug } };
        var book = new SpotBook([spot]) { Server = Server };

        Assert.Null(spot.GoneOn(Server));
        Assert.Equal(dug, spot.GoneOn("pwclassic136"));

        // Слияние с файлом другой копии тоже держит копки по серверам
        book.Merge([new ResourceSpot { Name = "Шалфей", Seen = 2, Gone = { [Server] = dug.AddMinutes(1) } }]);
        Assert.Equal((dug.AddMinutes(1), dug), (spot.GoneOn(Server)!.Value, spot.GoneOn("pwclassic136")!.Value));
    }

    [Fact]
    public void SpotsOfOneAreaAreGluedTogether()
    {
        // Раньше одна точка на место: возродившийся в 40 м ресурс с тем же номером заводил вторую точку
        var a = new ResourceSpot { Name = "Шалфей", X = 0, Seen = 3, Ids = { [Server] = 7 } };
        var b = new ResourceSpot { Name = "Шалфей", X = 40, Seen = 1, Ids = { [Server] = 7, ["pwclassic136"] = 9 }, Gone = { [Server] = _world.Time } };
        var other = new ResourceSpot { Name = "Шалфей", X = 20, Seen = 1, Ids = { [Server] = 8 } };
        var book = new SpotBook([a, b, other]) { Server = Server };

        book.Consolidate();

        Assert.Equal([a, other], book.Spots);
        Assert.Equal((10f, 4, 9u), (a.X, a.Seen, a.Ids["pwclassic136"]));
        Assert.Equal(_world.Time, a.GoneOn(Server));
        Assert.True(book.Changed);
    }

    [Fact]
    public void JournalWritesHeaderOnceAndOneLinePerEvent()
    {
        var journal = new SpotJournal(_file);
        var e = new SpotEvent(new DateTime(2026, 10, 3, 13, 55, 16), SpotEventKind.Respawned, "Высохший древесный корень", 0xC0100E80,
            new Position(-136.7f, 237.3f, 71.5f), new Position(-140.1f, 236.1f, 53.4f), 18.4f, TimeSpan.FromMinutes(10.27), 35.2f);
        journal.Write(e, "comeback146", "ClaudeCot");
        journal.Write(e with { Kind = SpotEventKind.Dug, SinceDug = null }, "comeback146", "ClaudeCot");

        var lines = File.ReadAllLines(_file);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("время;сервер;персонаж;событие", lines[0]);
        Assert.Equal("2026-10-03 13:55:16;comeback146;ClaudeCot;появился;Высохший древесный корень;0xC0100E80;-136.7;71.5;237.3;-140.1;53.4;18.4;10.27;35.2;;;", lines[1]);
        Assert.Contains(";выкопан;", lines[2]);
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

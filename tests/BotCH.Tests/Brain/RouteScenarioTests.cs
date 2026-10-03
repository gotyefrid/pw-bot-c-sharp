using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Brain;

/// <summary>Режим «Собирать ресурсы»: обход точек по кругу, копание у точки, только защита.</summary>
public class RouteScenarioTests
{
    private const uint Pickaxe = 3073;
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private BotBrain? _brain;
    private string? _stopped;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public RouteScenarioTests()
    {
        _settings.Mode = BotMode.GatherResources;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _world.Bag.Add(new InventoryItem(5, Pickaxe, 0, 1, null, null));
    }

    private BotBrain Brain
    {
        get
        {
            if (_brain is not null)
                return _brain;
            _brain = new BotBrain(new ActionRunner(_actions, NullLogger.Instance), Skills, _settings,
                new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), new Random(1), [Pickaxe], BotMode.GatherResources);
            _brain.StopRequested += reason => _stopped = reason;
            return _brain;
        }
    }

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    private void Route(params float[] xs)
        => _settings.Route.Points = xs.Select((x, i) => RoutePoint.At($"Точка {i + 1}", new Position(x, 0, 0))).ToList();

    private GroundItem AddResource(uint id, float x, string name)
    {
        var item = new GroundItem(0, id, 3079, GroundItemKind.Resource, new Position(x, 0, 0), Math.Abs(x - _world.Position.X), name);
        _world.Ground.Add(item);
        return item;
    }

    [Fact]
    public void PointsAreVisitedInOrderAndAfterLastComesFirst()
    {
        Route(50, 100);

        Tick();
        _world.Position = new Position(50, 0, 0);
        Tick(); // долетели — ищем ресурсы у точки
        Tick(); // нечего — к следующей
        _world.Position = new Position(100, 0, 0);
        Tick();
        Tick();

        Assert.Equal(
            ["move (50,0; 0,0; h 0,0) умно", "move (100,0; 0,0; h 0,0) умно", "move (50,0; 0,0; h 0,0) умно"],
            _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("На точке 1/2 — ищу: все ресурсы"));
        Assert.Contains(_log, e => e.Message.StartsWith("Точка 2 — здесь всё; дальше 1/2"));
    }

    [Fact]
    public void StartsFromChosenPoint()
    {
        Route(50, 100, 150);
        _settings.Route.StartIndex = 2;

        Tick();

        Assert.Equal(["move (150,0; 0,0; h 0,0) умно"], _actions.Calls);
        Assert.Contains(_log, e => e.Message.StartsWith("Обход: 3 точек, начинаю с 3-й"));
    }

    [Fact]
    public void StartedInAirFliesAtPointHeightAndTakesOffAgainIfLanded()
    {
        _settings.Route.Points = [RoutePoint.At("Над рудой", new Position(50, 30, 0)), RoutePoint.At("Дальше", new Position(100, 30, 0))];
        _world.Flying = true;

        Tick();
        Assert.Equal(["fly (50,0; 0,0; h 30,0)"], _actions.Calls);

        // Долетели, но оказались на земле — к следующей точке снова в воздух
        _world.Position = new Position(50, 0, 0);
        _world.Flying = false;
        Tick();
        Tick();
        Assert.Equal("fly-toggle", _actions.Calls.Last());
    }

    [Fact]
    public void DigsOnlyListedResourcesNearCurrentPoint()
    {
        Route(0, 300);
        _settings.Route.Points[0].ListMode = LootListMode.OnlyListed;
        _settings.Route.Points[0].Resources = ["Железная руда"];
        AddResource(0xC0000001, 10, "Шалфей");          // не в списке
        AddResource(0xC0000002, 80, "Железная руда");   // дальше радиуса (50 м) от точки
        AddResource(0xC0000003, 30, "Железная руда");

        Tick();
        Tick();

        Assert.Equal(["gather C0000003"], _actions.Calls);
        Assert.Contains(_log, e => e.Message == "Копаю Железная руда у точки 1/2, 30,0 м");
    }

    [Fact]
    public void ExceptListedSkipsDangerousResourceAtThisPointOnly()
    {
        // У точки 1 «Шалфей» часто у агро моба — «всё, кроме шалфея»; у точки 2 его копаем
        Route(0, 300);
        _settings.Route.Points[0].ListMode = LootListMode.ExceptListed;
        _settings.Route.Points[0].Resources = ["Шалфей"];
        AddResource(0xC0000001, 10, "Шалфей");
        AddResource(0xC0000002, 30, "Железная руда");

        Tick();
        Tick();

        Assert.Equal(["gather C0000002"], _actions.Calls);
        Assert.True(_settings.Route.Points[1].Wants("Шалфей"));
    }

    [Fact]
    public void EmptyListDigsAnyRegularResource()
    {
        Route(0, 300);
        AddResource(0xC0000001, 10, "Шалфей");

        Tick();
        Tick();

        Assert.Equal(["gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void NothingIsDugOnTheWayOnlyAtThePoint()
    {
        // Ресурс у точки виден издалека и в радиусе — но сначала долетаем, потом копаем
        Route(100, 300);
        AddResource(0xC0000001, 90, "Шалфей");

        Tick();
        Assert.Equal(["move (100,0; 0,0; h 0,0) умно"], _actions.Calls);

        _world.Position = new Position(100, 0, 0);
        Tick();
        Tick();
        Assert.Equal("gather C0000001", _actions.Calls.Last());
    }

    [Fact]
    public void ArrivedOnlyWhenFlightToPointEndsThenDigs()
    {
        // Над точкой по горизонтали, но ниже на 20 м — полёт ещё идёт: копание получило бы «занято», а это не «копать нечего».
        // Долетели (с высотой) — копаем камень у точки, к следующей не уходим
        _settings.Route.Points = [RoutePoint.At("1", new Position(50, 40, 0)), RoutePoint.At("2", new Position(200, 40, 0))];
        _world.Flying = true;
        Tick();
        _world.Position = new Position(50, 20, 0);
        AddResource(0xC0000001, 80, "Залежи камня");
        Tick();
        Tick();
        Assert.Equal(["fly (50,0; 0,0; h 40,0)"], _actions.Calls);

        _world.Position = new Position(50, 40, 0);
        Tick();
        Tick();
        Tick();

        Assert.Equal(["fly (50,0; 0,0; h 40,0)", "gather C0000001"], _actions.Calls);
    }

    [Fact]
    public void OnlyDefendsAgainstAttackers()
    {
        Route(0, 300);
        _world.Position = new Position(0, 0, 0);
        _world.AddMob(0x80000001, "Волк", 10);                                     // мирно стоит — не трогаем
        Tick();
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("select"));

        _world.AddMob(0x80000002, "Кабан", 5, targetWid: FakeWorld.HostWid);     // напал
        Tick();
        Assert.Contains("select 80000002", _actions.Calls);
    }

    [Fact]
    public void UnreachablePointIsSkipped()
    {
        Route(50, 100);

        // Перс стоит на месте: бег «упирается» дважды — идём к следующей точке
        for (var i = 0; i < 40; i++)
            Tick();

        Assert.Contains("move (100,0; 0,0; h 0,0) умно", _actions.Calls);
        Assert.Contains(_log, e => e.Message.Contains("не дошли 2 раза подряд, пропускаю"));
    }

    [Fact]
    public void GatheringUsesItsOwnPetAndPotionsNotFarmOnes()
    {
        // В фарме мобов пет выключен — у обхода свой, включён: зовёт летающего (стартовали в воздухе)
        _settings.Normalize();
        _settings.Route.Pet.Enabled = true;
        _settings.Route.Pet.AirPet = "Пчела";
        Route(0, 300);
        _world.Flying = true;
        _world.SetPets(null, new PetInCage(1, 1, 0) { Name = "Скорпион", Habitat = PetHabitat.Ground },
            new PetInCage(2, 1, 0) { Name = "Пчела", Habitat = PetHabitat.Air });

        Tick();

        Assert.False(_settings.Pet.Enabled);
        Assert.Equal(["summon 2"], _actions.Calls);
    }

    [Fact]
    public void GatheringSettingsStartAsCopyOfFarmThenLiveSeparately()
    {
        var settings = new BotSettings();
        settings.Combat.AttackSkillId = 299;
        settings.Pet.HealPercent = 55;

        settings.Normalize();
        Assert.Equal((299, 55), (settings.Route.Combat.AttackSkillId, settings.Route.Pet.HealPercent));

        settings.Route.Combat.AttackSkillId = 300;
        var again = SettingsJson.Parse(SettingsJson.Serialize(settings));
        Assert.Equal((299, 300), (again.Combat.AttackSkillId, again.Route.Combat.AttackSkillId));
        Assert.Equal(300, again.ForGathering().Combat.AttackSkillId);
    }

    [Theory]
    [InlineData(false, "маршрут пуст")]
    [InlineData(true, "нет кирки в сумке")]
    public void StopsWithReason(bool withPoints, string reason)
    {
        if (withPoints)
        {
            Route(50);
            _world.Bag.Clear();
        }

        Tick();

        Assert.StartsWith(reason, _stopped);
        Assert.Empty(_actions.Calls);
    }
}

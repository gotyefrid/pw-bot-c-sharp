using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>«Ремонтировать снаряжение»: из сумки или у NPC, с перелётом туда и обратно на фарм.</summary>
public class RepairBehaviorTests
{
    private static readonly ClassSkills Skills = new() { HealPet = 330, RevivePet = 329, DefaultAttack = 299, NotAttack = [167, 329, 330] };
    private const int SmithTid = 77;

    private readonly FakeWorld _world = new();
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;
    private int _seen;
    private int _flown;
    private int _walked;
    // Сервер чинит (нет — денег не хватило: команда ушла, а прочность та же)
    private bool _repairs = true;
    // Лавка из сумки открыта: без неё сервер из сумки не чинит и отвечает в чате
    private bool _shopOpen;
    // Лавка открывается по кнопке (нет — кнопка нажата, а окна нет)
    private bool _shopOpens = true;
    private bool _fromBag;
    private int _chatNumber;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public RepairBehaviorTests()
    {
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Potions.UseHp = false;
        _settings.Repair.Enabled = true;
        _settings.Repair.BelowPercent = 5;
        _settings.Repair.FlightHeight = 23;
        _world.HasFlyer = true;
        Wear(400, 10000);
    }

    private void Wear(int now, int max, bool noRepair = false, int slot = 0)
    {
        _world.Equipment.RemoveAll(i => i.Slot == slot);
        _world.Equipment.Add(new InventoryItem(slot, 100 + (uint)slot, 0, 1, null, null) { Durability = now, MaxDurability = max, NoRepair = noRepair });
    }

    // NPC стоит в игре здесь и выбран для ремонта (запомнили, где он стоял)
    // tid 0 — сервер вид NPC не показывает (1.3.6): ищем по имени
    private void Smith(Position at, bool inWorld = true, int tid = SmithTid)
    {
        _settings.Repair.Npc = RepairNpc.At(tid, "Кузнец Чан Ху", at);
        if (inWorld)
        {
            _world.Npcs.Add(new NpcInfo(0x5001, 5001, NpcKind.Npc, NpcState.Standing, 0, at with { X = at.X + 3 }, "Банкир", 0));
            _world.Npcs.Add(new NpcInfo(0x5000, 5000, NpcKind.Npc, NpcState.Standing, 0, at, "Кузнец Чан Ху", 0) { Tid = tid });
        }
    }

    private IBotRunner Brain => _brain ??= BotModes.Create(BotMode.FarmMobs, new ActionRunner(new GameControl(_actions), NullLogger.Instance),
        Skills, _settings, new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), random: new Random(1));

    // Шаг мозга, потом ответ «игры»: взлёт, полёт, ход, ремонт
    private void Tick()
    {
        Brain.Tick(_world.Wait(0.25).Snapshot());
        foreach (var call in _actions.Calls.Skip(_seen).ToList())
        {
            switch (call.Split(' ')[0])
            {
                case "fly-toggle":
                    _world.Flying = _world.Flying != true;
                    if (_world.Flying == false)
                        _world.Position = _world.Position with { Height = 0 };
                    break;
                case "fly":
                    _world.Position = _actions.FlyPoints[_flown++];
                    break;
                case "move":
                    _world.Position = _actions.MovePoints[_walked++];
                    break;
                case "shop":
                    _shopOpen = _shopOpens;
                    break;
                case "shop-close":
                    _shopOpen = false;
                    break;
                case "repair-all" when _fromBag && !_shopOpen:
                    _world.Chat.Add(new ChatMessage(++_chatNumber, 10, "Служба недоступна", 0));
                    break;
                case "repair-all" when _repairs:
                    Wear(10000, 10000);
                    break;
            }
        }
        _seen = _actions.Calls.Count;
    }

    private void TickFor(double seconds)
    {
        for (var t = 0.0; t < seconds; t += 0.25)
            Tick();
    }

    private bool Logged(string text) => _log.Any(e => e.Message.Contains(text));

    private int Count(string call) => _actions.Calls.Count(c => c == call);

    private void FromBag() => _settings.Repair.FromBag = _fromBag = true;

    [Fact]
    public void FromBagOpensShopRepairsAndClosesIt()
    {
        FromBag();

        TickFor(8);

        Assert.Equal(new[] { "shop", "repair-all", "shop-close" }, _actions.Calls);
        Assert.False(_shopOpen);
        Assert.Equal(100, RepairBehavior.WornPercent(_world.Snapshot()));
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("fly"));
        Assert.True(Logged("Починил"));
    }

    [Fact]
    public void FromBagWaitsForShopWindowBeforeRepair()
    {
        // Окно лавки открывает ответ сервера: «Починить всё» — не сразу после кнопки
        FromBag();

        TickFor(1.75);

        Assert.Equal(new[] { "shop" }, _actions.Calls);
    }

    [Fact]
    public void NotWornEnoughDoesNothing()
    {
        FromBag();
        Wear(600, 10000);

        TickFor(4);

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void NoRepairItemsDoNotCount()
    {
        FromBag();
        Wear(10000, 10000);
        Wear(0, 10000, noRepair: true, slot: 3);

        TickFor(4);

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void OffDoesNothing()
    {
        _settings.Repair.Enabled = false;
        _settings.Repair.FromBag = true;

        TickFor(4);

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void FarNpcFliesThereRepairsAndFliesBackToFarm()
    {
        Smith(new Position(500, 0, 0));

        TickFor(30);

        Assert.Equal(1, Count("npc-hello"));
        Assert.Equal(1, Count("repair-all"));
        Assert.Equal(1, Count("npc-back"));
        Assert.Contains(_actions.FlyPoints, p => p.HorizontalDistanceTo(new Position(500, 0, 0)) < 1);
        // Обратно — в центр фарма: точка старта
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= FlightTrip.Arrived);
        Assert.True(Logged("Вернулся на фарм"));
    }

    [Fact]
    public void NearNpcWalksWithoutFlying()
    {
        Smith(new Position(20, 0, 0));

        TickFor(8);

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("fly"));
        Assert.Equal(1, Count("npc-hello"));
        Assert.Equal(1, Count("repair-all"));
    }

    [Fact]
    public void HoveringAboveNpcLandsBeforeTalking()
    {
        // Висим в воздухе в 11 м над кузнецом: по карте он рядом, но из воздуха сервер говорить не даёт
        Smith(new Position(2, 0, 0));
        _world.Flying = true;
        _world.Position = new Position(0, 11, 0);

        TickFor(8);

        var landed = _actions.Calls.IndexOf("fly-toggle");
        var hello = _actions.Calls.IndexOf("npc-hello");
        Assert.True(landed >= 0 && hello > landed, string.Join(", ", _actions.Calls));
        Assert.Equal(1, Count("repair-all"));
    }

    [Fact]
    public void HoveringNearNpcFliesOverHimAndLands()
    {
        Smith(new Position(20, 0, 0));
        _world.Flying = true;
        _world.Position = new Position(0, 11, 0);

        TickFor(8);

        // Подлетел на своей высоте, а не вниз по прямой
        Assert.Contains(_actions.FlyPoints, p => p.HorizontalDistanceTo(new Position(20, 0, 0)) <= RepairBehavior.TalkDistance && p.Height == 11);
        Assert.True(_actions.Calls.IndexOf("fly-toggle") < _actions.Calls.IndexOf("npc-hello"));
        Assert.Equal(1, Count("repair-all"));
    }

    [Fact]
    public void NpcFarBelowOnGroundIsNotTalkedTo()
    {
        // Стоим на крыше над кузнецом: вниз не спуститься — не говорим и не чиним
        Smith(new Position(2, 0, 0));
        _world.Flying = false;
        _world.Position = new Position(0, 10, 0);

        TickFor(8);

        Assert.Equal(0, Count("npc-hello"));
        Assert.True(Logged("ниже на 10 м"));
    }

    [Fact]
    public void NpcWithoutTidIsFoundByName()
    {
        Smith(new Position(20, 0, 0), tid: 0);

        TickFor(8);

        Assert.Equal(1, Count("npc-hello"));
        Assert.Equal(1, Count("repair-all"));
    }

    [Fact]
    public void NoNpcChosenWarnsOnce()
    {
        TickFor(4);

        Assert.Empty(_actions.Calls);
        Assert.Single(_log, e => e.Message.Contains("не выбран NPC"));
    }

    [Fact]
    public void NpcMissingGivesUpAndFliesBack()
    {
        Smith(new Position(500, 0, 0), inWorld: false);

        TickFor(30);

        Assert.True(Logged("на месте нет"));
        Assert.Equal(0, Count("npc-hello"));
        Assert.True(_world.Position.HorizontalDistanceTo(new Position(0, 0, 0)) <= FlightTrip.Arrived);
    }

    [Fact]
    public void TooFarNpcIsNotVisited()
    {
        Smith(new Position(DeathReturnBehavior.MaxTrip + 100, 0, 0));

        TickFor(4);

        Assert.True(Logged("слишком далеко"));
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void BagOptionWithoutServerSupportGoesToNpc()
    {
        _actions.Capabilities = Capabilities.All.Without(Capability.RemoteRepair, "нет лавки из сумки");
        _settings.Repair.FromBag = true;
        Smith(new Position(3, 0, 0));

        TickFor(6);

        Assert.Equal(0, Count("shop"));
        Assert.Equal(1, Count("npc-hello"));
    }

    [Fact]
    public void FromBagNotRepairedClosesShopAndRetriesLessOften()
    {
        FromBag();
        _repairs = false;

        TickFor(8);

        Assert.True(Logged("прочность не выросла — попробую снова через 10 с"));
        Assert.Equal(new[] { "shop", "repair-all", "shop-close" }, _actions.Calls);

        TickFor(40);

        Assert.Equal(3, Count("repair-all"));
        Assert.True(Logged("через 20 с"));
        Assert.True(Logged("через 40 с"));
        Assert.Equal(3, Count("shop-close"));
    }

    [Fact]
    public void FromBagRetryWaitStopsGrowingAtOneMinute()
    {
        FromBag();
        _repairs = false;

        TickFor(4 * 60);

        Assert.True(Logged("через 60 с"));
        Assert.DoesNotContain(_log, e => e.Message.Contains("через 80 с") || e.Message.Contains("через 120 с"));
    }

    [Fact]
    public void FromBagRepairsOnRetryAndGoesBackToFarming()
    {
        FromBag();
        _repairs = false;
        TickFor(8);

        _repairs = true;
        TickFor(15);

        Assert.Equal(2, Count("repair-all"));
        Assert.True(Logged("Починил"));
        Assert.Equal(100, RepairBehavior.WornPercent(_world.Snapshot()));
    }

    [Fact]
    public void ServerReplyFromChatGoesToLog()
    {
        // Как было в игре: лавки нет — сервер на «Починить всё» пишет в чат «Служба недоступна»; это и есть причина в логе
        FromBag();
        _shopOpens = false;
        _world.Chat.Add(new ChatMessage(++_chatNumber, 10, "Выполняется другая операция", 0));

        TickFor(8);

        Assert.True(Logged("прочность не выросла — сервер: «Служба недоступна» — попробую снова через 10 с"));
        // Старое сообщение (до ремонта) — не ответ
        Assert.DoesNotContain(_log, e => e.Message.Contains("Выполняется другая операция"));
    }

    [Fact]
    public void ShopOpenRefusedRetriesWithoutClosing()
    {
        FromBag();
        _actions.Refuse.Add("shop");

        TickFor(4);

        Assert.True(Logged("лавка не открылась"));
        Assert.Equal(0, Count("repair-all"));
        Assert.Equal(0, Count("shop-close"));
    }

    [Fact]
    public void RefusedRepairCommandClosesShop()
    {
        FromBag();
        _actions.Refuse.Add("repair-all");

        TickFor(8);

        Assert.True(Logged("«Починить всё» не ушло"));
        Assert.Equal(new[] { "shop", "repair-all", "shop-close" }, _actions.Calls);
    }

    [Fact]
    public void StillWornAtNpcGivesUpUntilStart()
    {
        Smith(new Position(20, 0, 0));
        _repairs = false;

        TickFor(60);

        Assert.True(Logged("прочность не выросла"));
        Assert.Equal(1, Count("repair-all"));
    }

    [Fact]
    public void DrainDueGoesFirst()
    {
        FromBag();
        _settings.ExpDrain.Enabled = true;
        _settings.ExpDrain.MaxLevel = _world.Level;
        _world.Exp = 90;
        _world.ExpToLevel = 100;

        TickFor(3);

        Assert.Equal(0, Count("shop"));
    }
}

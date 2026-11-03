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
    private void Smith(Position at, bool inWorld = true)
    {
        _settings.Repair.Npc = RepairNpc.At(SmithTid, "Кузнец", at);
        if (inWorld)
            _world.Npcs.Add(new NpcInfo(0x5000, 5000, NpcKind.Npc, NpcState.Standing, 0, at, "Кузнец", 0) { Tid = SmithTid });
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

    [Fact]
    public void FromBagRepairsWithoutOpeningShop()
    {
        _settings.Repair.FromBag = true;

        TickFor(6);

        Assert.Equal(new[] { "repair-all" }, _actions.Calls);
        Assert.Equal(100, RepairBehavior.WornPercent(_world.Snapshot()));
        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("fly"));
        Assert.True(Logged("Починил"));
    }

    [Fact]
    public void NotWornEnoughDoesNothing()
    {
        _settings.Repair.FromBag = true;
        Wear(600, 10000);

        TickFor(4);

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void NoRepairItemsDoNotCount()
    {
        _settings.Repair.FromBag = true;
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
    public void StillWornAfterRepairGivesUpUntilStart()
    {
        _settings.Repair.FromBag = true;
        _repairs = false;

        TickFor(15);

        Assert.True(Logged("прочность не выросла"));
        Assert.Equal(1, Count("repair-all"));
    }

    [Fact]
    public void RefusedCommandGivesUp()
    {
        _settings.Repair.FromBag = true;
        _actions.Refuse.Add("repair-all");

        TickFor(6);

        Assert.True(Logged("не ушло"));
        Assert.Equal(1, Count("repair-all"));
    }

    [Fact]
    public void DrainDueGoesFirst()
    {
        _settings.Repair.FromBag = true;
        _settings.ExpDrain.Enabled = true;
        _settings.ExpDrain.MaxLevel = _world.Level;
        _world.Exp = 90;
        _world.ExpToLevel = 100;

        TickFor(3);

        Assert.Equal(0, Count("shop"));
    }
}

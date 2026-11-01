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

/// <summary>«Убирать в банк»: открыть банк, доложить на стопки, остальное — в пустые ячейки, закрыть.</summary>
public class StashBehaviorTests
{
    private readonly FakeWorld _world = new() { BagSlots = 32, BankSlots = 4 };
    private readonly FakeActions _actions = new();
    private readonly List<LogEntry> _log = [];
    private readonly BotSettings _settings = new();
    private readonly WorldRequests _requests = new();
    private IBotRunner? _brain;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public StashBehaviorTests()
    {
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Loot.StashToBank = true;
        _settings.Loot.StashNames = ["Предметы ДК", "Карта жизни"];
    }

    private IBotRunner Brain => _brain ??= BotModes.Create(BotMode.FarmMobs,
        new ActionRunner(new GameControl(_actions, requests: _requests), NullLogger.Instance), new ClassSkills(), _settings,
        new Logger { MinLevel = LogLevel.Debug }.AddSink(new ListSink(_log)).For("мозг"), random: new Random(1));

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    private static InventoryItem Item(int slot, uint tid, int count, string name, string? group = null, int max = 9999)
        => new(slot, tid, 8, count, null, null) { Name = name, Group = group, MaxCount = max };

    private void Put(InventoryItem item) => _world.Bag.Add(item);

    // Банк «открылся»: клиент получил содержимое
    private void BankArrives(params InventoryItem[] items)
    {
        _world.Bank = items.ToList();
        Tick(2);
    }

    [Fact]
    public void NothingChosenInBag_BankStaysClosed()
    {
        Put(Item(3, 805, 1, "Гравий"));

        Tick();
        Tick();

        Assert.Empty(_actions.Calls);
        Assert.False(_requests.Bank);
    }

    // Банк открывается, когда персонаж постоял без дела секунду
    private void Open()
    {
        Tick();
        Assert.DoesNotContain("bank", _actions.Calls);
        Tick(1);
        Assert.Equal("bank", _actions.Calls.Last());
        Assert.True(_requests.Bank);
    }

    [Fact]
    public void TopsUpStackThenPutsRestIntoFreeSlotThenCloses()
    {
        Put(Item(8, 8137, 74, "Мягкое сухожилие", "Предметы ДК", max: 100));
        Open();

        // В банке ячейка 0 — тот же предмет ×90 из 100, ячейка 1 занята другим
        BankArrives(Item(0, 8137, 90, "Мягкое сухожилие", "Предметы ДК", max: 100), Item(1, 805, 5, "Гравий"));
        Assert.Equal("bank-put-count 8 0 10", _actions.Calls.Last());

        // Доложили 10: в сумке 64, в банке стопка полна
        _world.Bag[0] = _world.Bag[0] with { Count = 64 };
        _world.Bank![0] = _world.Bank[0] with { Count = 100 };
        Tick();
        Tick(1);
        Assert.Equal("bank-exchange 8 2", _actions.Calls.Last());

        _world.Bag.Clear();
        Tick();
        Tick(1);
        Assert.Equal("cancel", _actions.Calls.Last());
        Assert.False(_requests.Bank);
        Assert.Contains(_log, e => e.Message.Contains("В банк: «Мягкое сухожилие» ×10"));
        Assert.Contains(_log, e => e.Message.Contains("В банк: «Мягкое сухожилие» ×64"));
    }

    [Fact]
    public void BankFull_LogsAndClosesAndDoesNotReopenUntilNextLoot()
    {
        Put(Item(3, 36309, 14, "Карта жизни"));
        Open();
        BankArrives(Item(0, 1, 1, "а", max: 1), Item(1, 2, 1, "б", max: 1), Item(2, 3, 1, "в", max: 1), Item(3, 4, 1, "г", max: 1));
        Tick();

        Assert.Equal(["bank", "cancel"], _actions.Calls);
        Assert.Single(_log, e => e.Message.Contains("Банк полон — «Карта жизни» ×14"));

        // Сумка изменилась, но нового лута не было — банк не открываем
        _world.Bag[0] = _world.Bag[0] with { Count = 15 };
        Tick(70);
        Tick(1);
        Assert.Equal(["bank", "cancel"], _actions.Calls);
    }

    [Fact]
    public void ServerSaysBusy_GivesUpUntilNextLootWithoutClosing()
    {
        Put(Item(3, 36309, 14, "Карта жизни"));
        Open();

        _world.Chat.Add(new ChatMessage(5, 10, "Выполняется другая операция", 0));
        Tick();
        Tick(5);

        Assert.Equal(["bank"], _actions.Calls);
        Assert.False(_requests.Bank);
        Assert.Contains(_log, e => e.Message.Contains("Банк не открылся: Выполняется другая операция"));
    }

    [Fact]
    public void WaitsUntilBodyIsFreeForASecond()
    {
        Put(Item(3, 36309, 14, "Карта жизни"));
        _world.Casting = true;
        Tick();
        Tick(2);
        Assert.Empty(_actions.Calls);

        _world.Casting = false;
        Open();
    }

    [Fact]
    public void Attacked_DoesNotOpen()
    {
        Put(Item(3, 36309, 14, "Карта жизни"));
        _world.AddMob(0x80100001, "Волк", 3, targetWid: FakeWorld.HostWid);

        Tick();
        Tick(1);
        Tick(1);

        Assert.DoesNotContain("bank", _actions.Calls);
    }

    [Fact]
    public void OpensAgainOnlyAfterLootFromKilledMob()
    {
        _settings.Target.KillMobs = true;
        _settings.Combat.UseSword = true;
        Tick();
        Tick(1);

        // Предмет появился между циклами — ждём конца лута
        Put(Item(3, 36309, 14, "Карта жизни"));
        var mob = _world.AddMob(0x80000001, "Волк", 3, hp: 100);
        _world.TargetWid = mob.Wid;
        Tick();
        Tick(1);
        Assert.DoesNotContain("bank", _actions.Calls);

        _world.Replace(mob, m => m with { State = NpcState.Dead });
        _world.TargetWid = 0;
        Tick();
        Tick();
        Tick(1);
        Assert.Equal("bank", _actions.Calls.Last());
    }

    [Fact]
    public void WithoutRemoteBank_DoesNothing()
    {
        _actions.Capabilities = Capabilities.All.Without(Capability.RemoteBank, "тест");
        Put(Item(3, 36309, 14, "Карта жизни"));

        Tick();

        Assert.Empty(_actions.Calls);
    }
}

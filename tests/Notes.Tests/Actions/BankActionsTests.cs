using System;
using Notes.Core.Actions;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Actions;

/// <summary>Положить в банк и взять из банка: что отправляется и когда считается сделанным.</summary>
public class BankActionsTests
{
    private const uint Tid = 806;
    private readonly FakeWorld _world = new();
    private readonly FakeActions _calls = new();

    private GameControl Control() => new(_calls);

    [Fact]
    public void PutSendsExchangeOfBagAndBankCell()
    {
        var item = new InventoryItem(11, Tid, 8, 100, null, null);
        _world.Bag.Add(item);

        var put = new BankPutAction(item, 7);
        Assert.Null(put.Precondition(_world.Snapshot()));
        Assert.True(put.Send(Control(), _world.Snapshot()).Ok);

        Assert.Equal(["bank-exchange 11 7"], _calls.Calls);
    }

    [Fact]
    public void PutIsDoneWhenCellIsEmptyOrHasOtherItem()
    {
        var item = new InventoryItem(11, Tid, 8, 100, null, null);
        _world.Bag.Add(item);
        var put = new BankPutAction(item, 7);
        var start = _world.Snapshot();

        Assert.Equal(Verdict.Pending, put.Check(start, _world.Snapshot()));

        _world.Bag.Clear();
        Assert.Equal(Verdict.Confirmed(), put.Check(start, _world.Snapshot()));
    }

    [Fact]
    public void PutRefusedWhenBagCellHasNoSuchItem()
        => Assert.NotNull(new BankPutAction(new InventoryItem(11, Tid, 8, 1, null, null), 7).Precondition(_world.Snapshot()));

    [Fact]
    public void TakeSendsExchangeAndIsDoneWhenItemAppearsInBagCell()
    {
        var take = new BankTakeAction(Tid, 7, 11);
        var start = _world.Snapshot();
        Assert.Null(take.Precondition(start));

        Assert.True(take.Send(Control(), start).Ok);
        Assert.Equal(["bank-exchange 11 7"], _calls.Calls);
        Assert.Equal(Verdict.Pending, take.Check(start, _world.Snapshot()));

        _world.Bag.Add(new InventoryItem(11, Tid, 8, 100, null, null));
        Assert.Equal(Verdict.Confirmed(), take.Check(start, _world.Snapshot()));
    }

    [Fact]
    public void TakeRefusedWhenBagCellIsBusy()
    {
        _world.Bag.Add(new InventoryItem(11, 5, 8, 1, null, null));

        Assert.NotNull(new BankTakeAction(Tid, 7, 11).Precondition(_world.Snapshot()));
    }
}

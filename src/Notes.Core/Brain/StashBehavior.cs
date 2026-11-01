using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// «Убирать в банк» (<see cref="LootSettings.StashToBank"/>): в сумке лежит выбранное (название или группа) — открыть банк из сумки,
/// доложить на неполные стопки того же предмета в банке, остальное — в пустые ячейки, закрыть банк «отменой» (как Esc). Только
/// основная вкладка банка без пароля; сумка — без надетого и квестового.
/// Ход не занимает: банк — своя линия действий, бой и лут идут рядом (от кражи предметы надо убрать и в бою). Не влезло (банк полон
/// для предмета) или не кладётся — пишет в лог один раз и пробует снова, только когда сумка изменилась, и не чаще раза в минуту.
/// </summary>
public sealed class StashBehavior : IBehavior
{
    // Банк открывает ответ сервера, в снимке окна не видно: ждём, пока клиент получит содержимое
    private static readonly TimeSpan OpenWait = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan OpenLimit = TimeSpan.FromSeconds(6);
    // Сумка подтвердила перенос раньше, чем, бывает, обновится банк, — до следующего переноса даём ему обновиться
    private static readonly TimeSpan AfterMove = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);

    private enum Stage { Idle, Opening, Moving, Closing }

    // Предметы (tid), которые в этот раз не убрать: места нет или не кладутся
    private readonly HashSet<uint> _stuck = [];

    private Stage _stage;
    private DateTime _openedAt;
    private DateTime _nextMove;
    private string? _gaveUpOn;
    private DateTime _gaveUpAt;

    public string Name => "банк";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var loot = c.Settings.Loot;
        var on = loot.StashToBank && c.Runner.Capabilities.Has(Capability.RemoteBank);
        if (c.Runner.IsPending(ActionSlot.Bank))
        {
            Status = "банк: жду";
            return false;
        }

        var w = c.World;
        switch (_stage)
        {
            case Stage.Idle:
                if (!on || !w.Inventory.Any(i => LootFilter.Stashes(loot, i)))
                    return false;
                if (_gaveUpOn is not null && (_gaveUpOn == BagPrint(w) || c.Now - _gaveUpAt < RetryAfter))
                    return false;

                _gaveUpOn = null;
                _stuck.Clear();
                c.Executor.Control.Requests.Bank = true;
                if (c.Send(new OpenBankAction()) != SubmitStatus.Sent)
                    return false;
                _stage = Stage.Opening;
                _openedAt = c.Now;
                c.Log.Info($"Убираю в банк: {string.Join(", ", w.Inventory.Where(i => LootFilter.Stashes(loot, i)).Select(Text))}");
                Status = "банк: открываю";
                return false;

            case Stage.Opening:
                if (!on)
                    return Close(c);
                if (c.Now - _openedAt >= OpenWait && w.BankSlots > 0 && w.Bank is not null)
                {
                    _stage = Stage.Moving;
                    return Move(c);
                }
                if (c.Now - _openedAt > OpenLimit)
                {
                    c.Say("stash-not-open", "Банк не открылся (клиент не получил его содержимое) — повторю позже", LogLevel.Warning, 3600);
                    _stuck.UnionWith(w.Inventory.Where(i => LootFilter.Stashes(loot, i)).Select(i => i.Tid));
                    return Close(c);
                }

                Status = "банк: открываю";
                return false;

            case Stage.Moving:
                return on ? Move(c) : Close(c);

            default:
                return Close(c);
        }
    }

    // Следующий предмет: на неполную стопку того же в банке, иначе в пустую ячейку; кончились — закрыть
    private bool Move(BrainContext c)
    {
        var w = c.World;
        if (c.Now < _nextMove)
        {
            Status = "банк: убираю";
            return false;
        }

        var item = w.Inventory.Where(i => LootFilter.Stashes(c.Settings.Loot, i) && !_stuck.Contains(i.Tid)).OrderBy(i => i.Slot).FirstOrDefault();
        if (item is null)
            return Close(c);

        var bank = w.Bank ?? [];
        Status = $"банк: убираю {Text(item)}";
        if (bank.FirstOrDefault(b => b.Tid == item.Tid && b.MaxCount > b.Count) is { } stack)
        {
            c.Send(new BankMergeAction(item, stack.Slot, Math.Min(item.Count, stack.MaxCount - stack.Count)));
            return false;
        }

        var used = new HashSet<int>(bank.Select(b => b.Slot));
        if (Enumerable.Range(0, w.BankSlots).Where(slot => !used.Contains(slot)).Select(slot => (int?)slot).FirstOrDefault() is { } free)
        {
            c.Send(new BankPutAction(item, free));
            return false;
        }

        c.Say($"stash-full-{item.Tid}", $"Банк полон — {Text(item)} не убираю", LogLevel.Warning, 3600);
        _stuck.Add(item.Tid);
        return false;
    }

    // Закрыть банк «отменой», когда тело свободно: она же прервала бы каст или копание
    private bool Close(BrainContext c)
    {
        _stage = Stage.Closing;
        if (c.Runner.BodyBusy(c.World) is { } busy)
        {
            Status = $"банк: закрою, когда освободится ({busy})";
            return false;
        }
        if (c.Send(new CloseBankAction()) != SubmitStatus.Sent)
            return false;

        _stage = Stage.Idle;
        c.Executor.Control.Requests.Bank = false;
        if (_stuck.Count > 0)
        {
            _gaveUpOn = BagPrint(c.World);
            _gaveUpAt = c.Now;
        }
        return false;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        var (item, count) = outcome.Action switch
        {
            BankPutAction put => (put.Item, put.Item.Count),
            BankMergeAction merge => (merge.Item, merge.Count),
            _ => (null, 0),
        };
        if (item is null)
            return;

        _nextMove = c.Now + AfterMove;
        if (outcome.Status == ActionStatus.Confirmed)
        {
            c.Log.Info($"В банк: {Text(item with { Count = count })}");
            return;
        }

        c.Say($"stash-fail-{item.Tid}", $"{Text(item)} не кладётся в банк ({outcome.Details}) — пропускаю", LogLevel.Warning, 3600);
        _stuck.Add(item.Tid);
    }

    // Сумка как была: изменилась — пробуем снова
    private static string BagPrint(WorldState w) => string.Join(";", w.Inventory.Select(i => $"{i.Slot}:{i.Tid}:{i.Count}"));

    private static string Text(InventoryItem i) => $"«{i.Name ?? $"tid {i.Tid}"}» ×{i.Count}";

    public void Reset()
    {
        _stage = Stage.Idle;
        _stuck.Clear();
        _gaveUpOn = null;
        _nextMove = DateTime.MinValue;
        Status = null;
    }
}

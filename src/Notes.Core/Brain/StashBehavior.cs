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
/// «Убирать в банк» (<see cref="LootSettings.StashToBank"/>): раз за цикл — при запуске и после сбора лута с каждого моба — если в
/// сумке лежит выбранное (название или группа), открыть банк из сумки, доложить на неполные стопки того же предмета в банке,
/// остальное — в пустые ячейки, закрыть банк «отменой» (как Esc). Только основная вкладка банка без пароля; сумка — без надетого
/// и квестового.
/// Сервер не открывает банк, пока идёт другое действие (бег, каст: «Выполняется другая операция»), поэтому банк открывается, когда
/// персонаж уже <see cref="Calm"/> стоит без дела, и пока бот с банком, ход занят — бой не бежит к следующему мобу. Напали —
/// банк бросаем (закроем, когда тело освободится), уберём после следующего лута. Не влезло (банк полон для предмета) или не
/// кладётся — пишет в лог один раз в час; те же предметы при той же сумке снова не пробует.
/// </summary>
public sealed class StashBehavior : IBehavior, IMultiStepBehavior
{
    // После бега сервер ещё с секунду отвечает «Выполняется другая операция»
    private static readonly TimeSpan Calm = TimeSpan.FromSeconds(1);
    // Банк открывает ответ сервера, в снимке окна не видно: ждём, пока клиент получит содержимое
    private static readonly TimeSpan OpenWait = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan OpenLimit = TimeSpan.FromSeconds(6);
    // Сумка подтвердила перенос раньше, чем, бывает, обновится банк, — до следующего переноса даём ему обновиться
    private static readonly TimeSpan AfterMove = TimeSpan.FromSeconds(0.5);

    // Ответы сервера в чате: банк не открылся или уже закрыт
    private static readonly string[] BankRefused = ["Выполняется другая операция", "Склад закрыт"];

    private enum Stage { Idle, Opening, Moving, Closing }

    // Предметы (tid), которые в этот раз не убрать: места нет или не кладутся
    private readonly HashSet<uint> _stuck = [];

    private Stage _stage;
    private bool _due = true;
    private int _kill;
    private DateTime? _calmSince;
    private DateTime _openedAt;
    private int _chatSeen;
    private DateTime _nextMove;
    private string? _gaveUpOn;

    public string Name => "банк";

    // Банк открыт или закрывается — обрывать нельзя: окно останется открытым, а перенос продолжится с середины
    public string? InMiddle => _stage == Stage.Idle ? null : "убираю в банк";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var loot = c.Settings.Loot;
        var on = loot.StashToBank && c.Runner.Capabilities.Has(Capability.RemoteBank);
        var w = c.World;

        // Новый цикл — лут с убитого моба собран
        if (c.Fight.Kill != _kill && !c.Fight.Busy)
        {
            _kill = c.Fight.Kill;
            _due = true;
        }

        var attacked = TargetSelector.Aggressor(w) is not null;
        if (_stage is Stage.Opening or Stage.Moving && (!on || attacked))
        {
            if (attacked)
                c.Log.Info("Напали — банк бросаю, уберу после следующего лута");
            _due = attacked;
            _stage = Stage.Closing;
        }

        if (c.Runner.IsPending(ActionSlot.Bank))
        {
            Status = "банк: жду";
            return _stage is Stage.Opening or Stage.Moving;
        }

        switch (_stage)
        {
            case Stage.Idle:
                return Start(c, on, attacked);

            case Stage.Opening:
                if (Refused(c) is { } refused)
                    return Failed(c, refused);
                if (c.Now - _openedAt >= OpenWait && w.BankSlots > 0 && w.Bank is not null)
                {
                    _stage = Stage.Moving;
                    return Move(c);
                }
                if (c.Now - _openedAt > OpenLimit)
                    return Failed(c, "клиент не получил содержимое банка");

                Status = "банк: открываю";
                return true;

            case Stage.Moving:
                return Refused(c) is { } closed ? Failed(c, closed) : Move(c);

            default:
                Close(c);
                return false;
        }
    }

    // Пора (цикл, есть что убрать, не напали, бой и лут кончились) — дождаться, пока персонаж постоит без дела, и открыть банк
    private bool Start(BrainContext c, bool on, bool attacked)
    {
        var w = c.World;
        if (!on || !_due || attacked || c.Fight.Busy)
        {
            _calmSince = null;
            return false;
        }

        var wanted = w.Inventory.Where(i => LootFilter.Stashes(c.Settings.Loot, i)).ToList();
        // Убирать нечего; или в прошлый раз не влезло ровно это, а сумка с тех пор та же
        if (wanted.Count == 0 || _gaveUpOn == BagPrint(w))
        {
            _due = false;
            _calmSince = null;
            return false;
        }

        if (c.Runner.BodyBusy(w) is { } busy)
        {
            _calmSince = null;
            Status = $"банк: жду, пока освободится ({busy})";
            return true;
        }

        _calmSince ??= c.Now;
        if (c.Now - _calmSince < Calm)
        {
            Status = "банк: жду";
            return true;
        }

        _gaveUpOn = null;
        _stuck.Clear();
        c.Executor.Control.Requests.Bank = true;
        if (c.Send(new OpenBankAction()) != SubmitStatus.Sent)
            return false;

        _stage = Stage.Opening;
        _openedAt = c.Now;
        _chatSeen = w.Chat.Count == 0 ? 0 : w.Chat.Max(m => m.Number);
        c.Log.Info($"Убираю в банк: {string.Join(", ", wanted.Select(Text))}");
        Status = "банк: открываю";
        return true;
    }

    // Следующий предмет: на неполную стопку того же в банке, иначе в пустую ячейку; кончились — закрыть
    private bool Move(BrainContext c)
    {
        var w = c.World;
        Status = "банк: убираю";
        if (c.Now < _nextMove)
            return true;

        var item = w.Inventory.Where(i => LootFilter.Stashes(c.Settings.Loot, i) && !_stuck.Contains(i.Tid)).OrderBy(i => i.Slot).FirstOrDefault();
        if (item is null)
        {
            _due = false;
            _stage = Stage.Closing;
            return Close(c);
        }

        var bank = w.Bank ?? [];
        Status = $"банк: убираю {Text(item)}";
        if (bank.FirstOrDefault(b => b.Tid == item.Tid && b.MaxCount > b.Count) is { } stack)
        {
            c.Send(new BankMergeAction(item, stack.Slot, Math.Min(item.Count, stack.MaxCount - stack.Count)));
            return true;
        }

        var used = new HashSet<int>(bank.Select(b => b.Slot));
        if (Enumerable.Range(0, w.BankSlots).Where(slot => !used.Contains(slot)).Select(slot => (int?)slot).FirstOrDefault() is { } free)
        {
            c.Send(new BankPutAction(item, free));
            return true;
        }

        c.Say($"stash-full-{item.Tid}", $"Банк полон — {Text(item)} не убираю", LogLevel.Warning, 3600);
        _stuck.Add(item.Tid);
        return true;
    }

    // Сервер ответил в чате, что банк не открыт: бросаем до следующего цикла. Окна нет — и закрывать нечего
    private bool Failed(BrainContext c, string why)
    {
        c.Say("stash-refused", $"Банк не открылся: {why} — уберу после следующего лута", LogLevel.Warning, 600);
        _stage = Stage.Idle;
        _due = false;
        c.Executor.Control.Requests.Bank = false;
        return false;
    }

    private string? Refused(BrainContext c)
        => c.World.Chat.Where(m => m.Number > _chatSeen).Select(m => m.Plain)
            .FirstOrDefault(text => BankRefused.Any(r => text.IndexOf(r, StringComparison.OrdinalIgnoreCase) >= 0));

    // Закрыть банк «отменой», когда тело свободно: она же прервала бы каст или копание. true — ход занят (ждём, пока пошлём)
    private bool Close(BrainContext c)
    {
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
            _gaveUpOn = BagPrint(c.World);
        return true;
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
        _due = true;
        _calmSince = null;
        _stuck.Clear();
        _gaveUpOn = null;
        _nextMove = DateTime.MinValue;
        Status = null;
    }
}

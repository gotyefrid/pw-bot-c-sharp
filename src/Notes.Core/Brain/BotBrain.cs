using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.Stats;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Мозг бота — один цикл для любого режима: на каждом снимке — итоги ждущих действий хозяевам, затем поведения по порядку,
/// первое занявшее ход останавливает перебор. Какие поведения и в каком порядке, решает режим (<see cref="BotModes"/>):
/// мозг о режимах не знает. Один поток решений: <see cref="Tick"/> вызывается из потока снимков. После хода — статистика
/// (<see cref="StatsWatcher"/>), если её ведут: поведения о ней не знают.
/// </summary>
public sealed class BotBrain : IBotRunner
{
    private readonly object _lock = new();
    private readonly BrainContext _context;
    private readonly IReadOnlyList<IBehavior> _behaviors;
    private readonly IBehavior _main;
    private readonly StatsWatcher _stats = new();
    private BotSettings? _newSettings;
    private string _status = "ожидание";

    /// <param name="context">Контекст хода; настройки в нём — уже копия (окно может менять свои дальше).</param>
    /// <param name="behaviors">Поведения по приоритету: первое занявшее ход останавливает перебор.</param>
    /// <param name="main">Чей статус показывать, когда ход никто не занял (у фарма и обхода — бой: «ищу цель…»).</param>
    public BotBrain(BrainContext context, IReadOnlyList<IBehavior> behaviors, IBehavior main)
    {
        if (!behaviors.Contains(main))
            throw new ArgumentException("Главное поведение должно быть в наборе", nameof(main));

        _context = context;
        _behaviors = behaviors;
        _main = main;
    }

    /// <summary>Что делает бот — для окна.</summary>
    public string Status => _status;

    public event Action<string>? StatusChanged;

    /// <summary>Мозг сам просит остановку (персонаж погиб или поведение попросило).</summary>
    public event Action<string>? StopRequested;

    /// <summary>Новые настройки применяются на следующем шаге (копия — окно может менять свои дальше).</summary>
    public void UpdateSettings(BotSettings settings)
    {
        lock (_lock)
            _newSettings = settings.Clone();
    }

    public void Tick(WorldState world)
    {
        string? stop = null;
        lock (_lock)
        {
            if (_newSettings is not null)
            {
                _context.Settings = _newSettings;
                _newSettings = null;
            }

            _context.World = world;
            _context.StartPosition ??= world.Host.Position;

            _context.Executor.Update(world);
            if (_context.SkillRanges.Observe(world, _context.Skills.HealPet) is { } learned)
                _context.Log.Info(learned);
            Deliver();
            _context.InMiddle = _behaviors.OfType<IMultiStepBehavior>().Select(b => b.InMiddle).FirstOrDefault(s => s is not null);

            if (world.Host.IsDead)
            {
                if (TickDead() is { } handler)
                    SetStatus(handler.Status ?? "персонаж мёртв");
                else
                {
                    stop = "персонаж погиб";
                    SetStatus("персонаж погиб — стоп");
                }
            }
            else
            {
                IBehavior? acted = null;
                foreach (var behavior in _behaviors)
                {
                    _context.Owner = behavior;
                    if (behavior.Tick(_context))
                    {
                        acted = behavior;
                        break;
                    }
                }

                _context.Owner = null;
                foreach (var watcher in _behaviors.OfType<IWatchingBehavior>())
                    watcher.Observe(_context, acted);
                Deliver();
                SetStatus((acted ?? _main).Status ?? _behaviors.Select(b => b.Status).FirstOrDefault(s => s is not null) ?? "ожидание");
            }

            Record(_stats.Observe(world, Draining, Collecting));

            // Остановку могут попросить и живым, и мёртвым (встать в городе не вышло)
            if (stop is null && _context.StopReason is { } reason)
            {
                stop = reason;
                SetStatus(reason + " — стоп");
            }
            _context.StopReason = null;
        }

        if (stop is not null)
        {
            _context.Log.Warning($"{char.ToUpper(stop[0])}{stop.Substring(1)} — бот остановлен");
            StopRequested?.Invoke(stop);
        }
    }

    /// <summary>Поведение с этой возможностью (например, обход — <see cref="IRouteProgress"/>).</summary>
    public T? Part<T>() where T : class => _behaviors.OfType<T>().FirstOrDefault();

    public void Reset()
    {
        lock (_lock)
        {
            Record(_stats.End());
            _context.Executor.Clear();
            _context.TakeOutcomes();
            _context.StartPosition = null;
            _context.StopReason = null;
            _context.Fight.Abort();
            _context.Fight.LootSpots.Clear();
            foreach (var behavior in _behaviors)
                behavior.Reset();
            SetStatus("ожидание");
        }
    }

    // Слив опыта идёт: смерти в нём — не «погиб на фарме»
    private bool Draining => _behaviors.OfType<ExpDrainBehavior>().Any(b => b.Active);

    // Бот ждёт подбора или копания: прибавка в сумке — добыча, а не покупка или банк
    private bool Collecting => _context.Runner.Pending.Any(a => a is PickupAction or GatherAction);

    private void Record(IReadOnlyList<StatEvent> events)
    {
        if (_context.Stats is not { } sink)
            return;
        foreach (var stat in events)
            sink.Add(stat);
    }

    // Мёртвый перс: ходят только поведения, которые знают, что делать после смерти; первое занявшее ход — её хозяин
    private IBehavior? TickDead()
    {
        IBehavior? handler = null;
        foreach (var behavior in _behaviors.OfType<IDeathBehavior>())
        {
            _context.Owner = behavior;
            if (behavior.TickDead(_context))
            {
                handler = behavior;
                break;
            }
        }

        _context.Owner = null;
        Deliver();
        return handler;
    }

    // Итоги действий — только хозяину: и завершившиеся по снимку, и «не отправлено», и «отменено» (вытеснило более важное)
    private void Deliver()
    {
        foreach (var outcome in _context.TakeOutcomes())
        {
            if (outcome.Action.Owner is not IBehavior owner)
                continue;

            _context.Owner = owner;
            owner.OnOutcome(_context, outcome);
        }

        _context.Owner = null;
    }

    private void SetStatus(string status)
    {
        if (status == _status)
            return;

        _status = status;
        StatusChanged?.Invoke(status);
    }
}

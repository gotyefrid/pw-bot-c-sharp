using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Logging;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Мозг бота — один цикл для любого режима: на каждом снимке — итоги ждущих действий хозяевам, затем поведения по порядку,
/// первое занявшее ход останавливает перебор. Какие поведения и в каком порядке, решает режим (<see cref="BotModes"/>):
/// мозг о режимах не знает. Один поток решений: <see cref="Tick"/> вызывается из потока снимков.
/// </summary>
public sealed class BotBrain : IBotRunner
{
    private readonly object _lock = new();
    private readonly BrainContext _context;
    private readonly IReadOnlyList<IBehavior> _behaviors;
    private readonly IBehavior _main;
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
            Deliver();

            if (world.Host.IsDead)
            {
                stop = "персонаж погиб";
                SetStatus("персонаж погиб — стоп");
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
                Deliver();
                SetStatus((acted ?? _main).Status ?? _behaviors.Select(b => b.Status).FirstOrDefault(s => s is not null) ?? "ожидание");
                if (_context.StopReason is { } reason)
                {
                    _context.StopReason = null;
                    stop = reason;
                    SetStatus(reason + " — стоп");
                }
            }
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
            _context.Executor.Clear();
            _context.TakeOutcomes();
            _context.StartPosition = null;
            _context.StopReason = null;
            _context.Fight.Abort();
            foreach (var behavior in _behaviors)
                behavior.Reset();
            SetStatus("ожидание");
        }
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

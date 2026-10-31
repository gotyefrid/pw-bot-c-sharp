using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.World;

namespace Notes.Core.Incidents;

/// <summary>
/// Запись мира вокруг смерти. Держит в памяти снимки за последние <see cref="Before"/> (старые вытесняются). Случилось
/// событие (<see cref="Mark"/> или HP персонажа упало до 0) — снимки до него откладываются и уже не вытесняются, запись идёт
/// ещё <see cref="After"/>, потом случай целиком уходит в <see cref="Closed"/>. Новое событие, пока запись идёт, попадает в
/// тот же случай и продлевает её (пет погиб, через 20 с — перс), но не дольше <see cref="MaxLength"/> от начала.
/// Память игры сам не читает: смотрит те же снимки, что и бот, — игру не нагружает. Вызывается из потока снимков.
/// </summary>
public sealed class IncidentRecorder
{
    private readonly object _lock = new();
    private readonly Queue<WorldState> _history = new();
    private WorldState? _last;
    private Recording? _open;

    public IncidentRecorder(TimeSpan? before = null, TimeSpan? after = null, TimeSpan? maxLength = null)
    {
        Before = before ?? TimeSpan.FromMinutes(1);
        After = after ?? TimeSpan.FromMinutes(1);
        MaxLength = maxLength ?? TimeSpan.FromMinutes(5);
    }

    public TimeSpan Before { get; }
    public TimeSpan After { get; }
    public TimeSpan MaxLength { get; }

    /// <summary>Случай записан до конца. Приходит из потока снимков: долгую работу (файл) — в другой поток.</summary>
    public event Action<Incident>? Closed;

    public void Observe(WorldState world)
    {
        Incident? closed;
        lock (_lock)
        {
            var previous = _last;
            Add(world);
            if (previous is { Host: { Hp: > 0 } } && world.Host is { IsDead: true, MaxHp: > 0 })
                MarkLocked(world, "HP персонажа упало до 0");
            closed = _open is { } open && world.Time >= open.Until ? CloseLocked() : null;
        }

        if (closed is not null)
            Closed?.Invoke(closed);
    }

    /// <summary>Событие для записи (строка — как в логе); <paramref name="world"/> — снимок, в котором его заметили.</summary>
    public void Mark(WorldState world, string text)
    {
        lock (_lock)
        {
            Add(world);
            MarkLocked(world, text);
        }
    }

    /// <summary>Закончить запись сейчас (отключение от клиента): что успели — уходит в <see cref="Closed"/>.</summary>
    public void Flush()
    {
        Incident? closed;
        lock (_lock)
            closed = _open is null ? null : CloseLocked();
        if (closed is not null)
            Closed?.Invoke(closed);
    }

    // Один снимок — один раз: «кто убил» может отметить событие тем же снимком, который придёт сюда следом
    private void Add(WorldState world)
    {
        if (ReferenceEquals(world, _last))
            return;

        _last = world;
        _open?.Snapshots.Add(world);
        _history.Enqueue(world);
        while (_history.Count > 0 && world.Time - _history.Peek().Time > Before)
            _history.Dequeue();
    }

    private void MarkLocked(WorldState world, string text)
    {
        var open = _open ??= new Recording([.. _history]);
        open.Events.Add(new IncidentEvent(world.Time, text, world));
        var start = open.Snapshots.Count > 0 ? open.Snapshots[0].Time : world.Time;
        var until = world.Time + After;
        open.Until = until - start > MaxLength ? start + MaxLength : until;
    }

    private Incident CloseLocked()
    {
        var open = _open!;
        _open = null;
        return new Incident(open.Events.ToList(), open.Snapshots.ToList());
    }

    private sealed class Recording(List<WorldState> snapshots)
    {
        public List<WorldState> Snapshots { get; } = snapshots;
        public List<IncidentEvent> Events { get; } = [];
        public DateTime Until { get; set; }
    }
}

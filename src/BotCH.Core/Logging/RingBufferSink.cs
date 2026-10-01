using System;
using System.Collections.Generic;

namespace BotCH.Core.Logging;

/// <summary>
/// Последние N записей в памяти — для окна. Старые вытесняются, поэтому окно не разрастается при долгой работе.
/// Событие <see cref="Added"/> приходит из потока бота: окно само переносит его в свой поток.
/// </summary>
public sealed class RingBufferSink(int capacity = 1000) : ILogSink
{
    private readonly Queue<LogEntry> _entries = new();
    private readonly object _lock = new();

    public int Capacity { get; } = capacity > 0 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));

    public event Action<LogEntry>? Added;

    public void Write(LogEntry entry)
    {
        lock (_lock)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }

        Added?.Invoke(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_lock)
            return [.. _entries];
    }
}

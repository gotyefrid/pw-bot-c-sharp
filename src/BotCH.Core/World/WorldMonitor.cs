using System;
using System.Threading;

namespace BotCH.Core.World;

/// <summary>
/// Читает снимок мира в фоне раз в <see cref="Period"/> и раздаёт его подписчикам (окну, позже — мозгу).
/// Ошибка чтения не останавливает цикл: приходит <see cref="Failed"/>, через период — новая попытка.
/// События приходят из фонового потока.
/// </summary>
public sealed class WorldMonitor(Func<WorldState> read, TimeSpan period) : IWorldFeed, IDisposable
{
    private readonly object _lock = new();
    private Thread? _thread;
    private CancellationTokenSource? _stop;

    public TimeSpan Period { get; } = period;

    /// <summary>Последний удачный снимок (null — ещё не было или последнее чтение не удалось).</summary>
    public WorldState? Last { get; private set; }

    public event Action<WorldState>? Updated;

    /// <summary>Чтение не удалось: текст для пользователя («Персонаж не в мире…»).</summary>
    public event Action<string>? Failed;

    public bool IsRunning
    {
        get
        {
            lock (_lock)
                return _thread is not null;
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_thread is not null)
                return;

            var stop = _stop = new CancellationTokenSource();
            _thread = new Thread(() => Loop(stop.Token)) { IsBackground = true, Name = "BotCH: снимок мира" };
            _thread.Start();
        }
    }

    public void Stop()
    {
        Thread? thread;
        lock (_lock)
        {
            thread = _thread;
            _stop?.Cancel();
            _thread = null;
        }

        if (thread is not null && thread != Thread.CurrentThread)
            thread.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose() => Stop();

    private void Loop(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var state = read();
                Last = state;
                Updated?.Invoke(state);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                Last = null;
                Failed?.Invoke(e.Message);
            }

            stop.WaitHandle.WaitOne(Period);
        }
    }
}

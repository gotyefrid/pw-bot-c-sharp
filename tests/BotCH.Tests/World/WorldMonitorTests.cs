using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.World;

public class WorldMonitorTests
{
    private static WorldState State(int hp) => new(
        DateTime.Now, TimeSpan.Zero,
        new HostState(0, 1, "Перс", 10, hp, 100, 0, null, default, 0, false, 0),
        [], [], [], [], null);

    private static void WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "Не дождались");
            Thread.Sleep(5);
        }
    }

    [Fact]
    public void SnapshotsArriveInBackground()
    {
        var hp = 0;
        var seen = new ConcurrentQueue<int>();
        using var monitor = new WorldMonitor(() => State(++hp), TimeSpan.FromMilliseconds(10));
        monitor.Updated += s => seen.Enqueue(s.Host.Hp);

        monitor.Start();
        WaitFor(() => seen.Count >= 3);
        monitor.Stop();

        Assert.Equal([1, 2, 3], seen.Take(3));
        Assert.False(monitor.IsRunning);
    }

    [Fact]
    public void FailureDoesNotStopMonitor()
    {
        var calls = 0;
        var errors = new ConcurrentQueue<string>();
        var updates = 0;
        using var monitor = new WorldMonitor(() =>
        {
            if (Interlocked.Increment(ref calls) == 1)
                throw new WorldNotReadyException("Персонаж не в мире");
            return State(50);
        }, TimeSpan.FromMilliseconds(10));
        monitor.Failed += errors.Enqueue;
        monitor.Updated += _ => Interlocked.Increment(ref updates);

        monitor.Start();
        WaitFor(() => Volatile.Read(ref updates) > 0);
        monitor.Stop();

        Assert.Equal("Персонаж не в мире", Assert.Single(errors));
        Assert.Equal(50, monitor.Last!.Host.Hp);
    }

    [Fact]
    public void StopIsQuickAndRepeatable()
    {
        using var monitor = new WorldMonitor(() => State(1), TimeSpan.FromMinutes(1));
        monitor.Start();
        monitor.Start();

        var started = DateTime.UtcNow;
        monitor.Stop();
        monitor.Stop();

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1), "Остановка не должна ждать конца периода");
    }
}

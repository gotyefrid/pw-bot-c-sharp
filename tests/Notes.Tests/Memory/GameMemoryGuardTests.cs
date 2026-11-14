using System;
using System.Collections.Generic;
using System.Diagnostics;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Xunit;

namespace Notes.Tests.Memory;

public class GameMemoryGuardTests
{
    private const long Mb = 1024 * 1024;
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0);

    private readonly List<LogEntry> _log = [];
    private FreeMemory? _free = new(500 * Mb, 100 * Mb);
    private int _queries;

    private GameMemoryGuard Guard()
        => new(() =>
        {
            _queries++;
            return _free;
        }, new Logger().AddSink(new ListSink(_log)).For("память"));

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    [Fact]
    public void PlentyOfMemoryIsQuiet()
    {
        Assert.Equal(GameMemoryState.Ok, Guard().Check(T0));
        Assert.Empty(_log);
    }

    [Fact]
    public void ChecksAtMostEvery30Seconds()
    {
        var guard = Guard();
        guard.Check(T0);
        guard.Check(T0.AddSeconds(10));
        guard.Check(T0.AddSeconds(29));
        Assert.Equal(1, _queries);

        guard.Check(T0.AddSeconds(30));
        Assert.Equal(2, _queries);
    }

    [Fact]
    public void LowMemoryAsksForRestartWhenFreeAndWarnsOnce()
    {
        _free = new FreeMemory(120 * Mb, 20 * Mb);
        var guard = Guard();

        Assert.Equal(GameMemoryState.Low, guard.Check(T0));
        Assert.Equal(GameMemoryState.Low, guard.Check(T0.AddMinutes(1)));
        Assert.Single(_log);
        Assert.Contains("мало памяти", _log[0].Message);
    }

    [Fact]
    public void LowStaysLowEvenIfMemoryCameBack()
    {
        _free = new FreeMemory(120 * Mb, 20 * Mb);
        var guard = Guard();
        guard.Check(T0);

        _free = new FreeMemory(500 * Mb, 100 * Mb);
        Assert.Equal(GameMemoryState.Low, guard.Check(T0.AddMinutes(1)));
    }

    [Fact]
    public void NoFreeMomentForFiveMinutesMakesItCritical()
    {
        _free = new FreeMemory(120 * Mb, 20 * Mb);
        var guard = Guard();
        guard.Check(T0);

        Assert.Equal(GameMemoryState.Low, guard.Check(T0 + GameMemoryGuard.LowWait - TimeSpan.FromSeconds(1)));
        Assert.Equal(GameMemoryState.Critical, guard.Check(T0 + GameMemoryGuard.LowWait));
        Assert.Contains(_log, e => e.Level == LogLevel.Warning && e.Message.Contains("перезапускаю клиент как есть"));
    }

    [Theory]
    [InlineData(40, 10)] // мало всего
    [InlineData(300, 0)] // много, но мелкими кусками
    public void CriticalNearTheLimit(long totalMb, long largestKb)
    {
        _free = new FreeMemory(totalMb * Mb, largestKb * 1024);
        var guard = Guard();

        Assert.Equal(GameMemoryState.Critical, guard.Check(T0));
        Assert.Equal(GameMemoryState.Critical, guard.Check(T0.AddSeconds(1)));
        Assert.Single(_log, e => e.Level == LogLevel.Error && e.Message.Contains("перезапускаю клиент сейчас"));
    }

    [Fact]
    public void NoAnswerFromWindowsChangesNothing()
    {
        _free = null;

        Assert.Equal(GameMemoryState.Ok, Guard().Check(T0));
        Assert.Empty(_log);
    }

    [Fact]
    public void OwnProcessHasFreeMemory()
    {
        using var process = GameProcess.Open(Process.GetCurrentProcess().Id);

        var free = new ProcessInspector(process).QueryFreeMemory();

        Assert.NotNull(free);
        Assert.InRange(free!.Value.Largest, 1, free.Value.Total);
        Assert.True(free.Value.Total > 50 * Mb, $"свободно {free.Value.TotalMb} МБ");
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using BotCH.Core.Logging;
using BotCH.Core.Memory;
using Xunit;

namespace BotCH.Tests.Memory;

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
        Assert.False(Guard().ShouldStop(T0));
        Assert.Empty(_log);
    }

    [Fact]
    public void ChecksAtMostEvery30Seconds()
    {
        var guard = Guard();
        guard.ShouldStop(T0);
        guard.ShouldStop(T0.AddSeconds(10));
        guard.ShouldStop(T0.AddSeconds(29));
        Assert.Equal(1, _queries);

        guard.ShouldStop(T0.AddSeconds(30));
        Assert.Equal(2, _queries);
    }

    [Fact]
    public void LowMemoryWarnsOnceIn10Minutes()
    {
        _free = new FreeMemory(120 * Mb, 20 * Mb);
        var guard = Guard();

        Assert.False(guard.ShouldStop(T0));
        Assert.False(guard.ShouldStop(T0.AddMinutes(1)));
        Assert.Single(_log, e => e.Level == LogLevel.Warning && e.Message.Contains("мало памяти"));

        guard.ShouldStop(T0.AddMinutes(11));
        Assert.Equal(2, _log.Count);
    }

    [Theory]
    [InlineData(40, 10)] // мало всего
    [InlineData(300, 0)] // много, но мелкими кусками
    public void StopsNearTheLimit(long totalMb, long largestKb)
    {
        _free = new FreeMemory(totalMb * Mb, largestKb * 1024);

        Assert.True(Guard().ShouldStop(T0));
        Assert.Single(_log, e => e.Level == LogLevel.Error && e.Message.Contains("Перезапустите клиент"));
    }

    [Fact]
    public void NoAnswerFromWindowsDoesNotStop()
    {
        _free = null;

        Assert.False(Guard().ShouldStop(T0));
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

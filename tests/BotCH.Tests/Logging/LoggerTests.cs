using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BotCH.Core.Logging;
using Xunit;

namespace BotCH.Tests.Logging;

public class LoggerTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0);

    private sealed class ListSink : ILogSink
    {
        public List<LogEntry> Entries { get; } = [];
        public void Write(LogEntry entry) => Entries.Add(entry);
    }

    private sealed class BrokenSink : ILogSink
    {
        public void Write(LogEntry entry) => throw new IOException("диск занят");
    }

    [Fact]
    public void EntryHasSourceLevelAndTime()
    {
        var sink = new ListSink();
        new Logger(() => Noon).AddSink(sink).For("пет").Warning("нет корма");

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(new LogEntry(Noon, LogLevel.Warning, "пет", "нет корма"), entry);
        Assert.Equal("12:00:00.000 WRN [пет] нет корма", entry.ToString());
    }

    [Fact]
    public void BelowMinLevelIsDropped()
    {
        var sink = new ListSink();
        var log = new Logger { MinLevel = LogLevel.Info }.AddSink(sink).For("бой");

        log.Debug("подробности");
        log.Info("цель выбрана");

        Assert.Equal(["цель выбрана"], sink.Entries.Select(e => e.Message));
    }

    [Fact]
    public void BrokenSinkDoesNotStopOthers()
    {
        var sink = new ListSink();
        var logger = new Logger().AddSink(new BrokenSink()).AddSink(sink);

        logger.For("бот").Error("что-то сломалось");

        Assert.Single(sink.Entries);
    }

    [Fact]
    public void RingBufferKeepsOnlyLastEntries()
    {
        var ring = new RingBufferSink(capacity: 3);
        var log = new Logger().AddSink(ring).For("t");
        var added = 0;
        ring.Added += _ => added++;

        for (var i = 1; i <= 5; i++)
            log.Info(i.ToString());

        Assert.Equal(["3", "4", "5"], ring.Snapshot().Select(e => e.Message));
        Assert.Equal(5, added);
    }

    [Fact]
    public void FileSinkWritesOneFilePerDay()
    {
        var directory = Path.Combine(Path.GetTempPath(), "botch-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sink = new DailyFileSink(directory);
            var time = Noon;
            var log = new Logger(() => time).AddSink(sink).For("бот");

            log.Info("первый день");
            time = Noon.AddDays(1);
            log.Info("второй день");
            log.Info("ещё");

            Assert.Equal(["12:00:00.000 INF [бот] первый день"], File.ReadAllLines(sink.PathFor(Noon)));
            Assert.Equal(2, File.ReadAllLines(sink.PathFor(Noon.AddDays(1))).Length);
            Assert.EndsWith("botch-2026-10-01.log", sink.PathFor(Noon));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileCanBeReadWhileBotWrites()
    {
        var directory = Path.Combine(Path.GetTempPath(), "botch-log-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sink = new DailyFileSink(directory);
            var log = new Logger(() => Noon).AddSink(sink).For("бот");
            log.Info("раз");

            // Блокнот/просмотрщик держит файл открытым — бот всё равно пишет
            using (new FileStream(sink.PathFor(Noon), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                log.Info("два");

            Assert.Equal(2, File.ReadAllLines(sink.PathFor(Noon)).Length);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using Notes.Core.Stats;
using Xunit;

namespace Notes.Tests.Stats;

public class StatsBookTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "notes-stats-" + Guid.NewGuid().ToString("N"));
    // Середина месяца по местному времени: события и «сейчас» в одном файле
    private static readonly DateTime Now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Local).ToUniversalTime();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private StatsBook Open()
    {
        var book = new StatsBook(_directory, Now.ToLocalTime());
        book.Load();
        return book;
    }

    private static StatEvent At(double minutes, StatKind kind, string name = "", long amount = 1)
        => new(Now.AddMinutes(minutes), kind, name, amount);

    [Fact]
    public void EventsSurviveReopen()
    {
        var book = Open();
        book.Add(At(-60, StatKind.SessionStart));
        book.Add(At(-50, StatKind.Kill, "Волк «вожак»"));
        book.Add(At(-50, StatKind.Item, "Мягкий мех", 3));
        book.Add(At(-30, StatKind.SessionEnd));

        var again = Open().Summary(StatsPeriod.Day, Now);

        Assert.Equal(TimeSpan.FromMinutes(30), again.Worked);
        Assert.Equal(1, again.Kills);
        Assert.Equal(new ItemCount("Мягкий мех", 3), Assert.Single(again.Items));
        Assert.Single(Directory.GetFiles(_directory, "2026-11.jsonl"));
    }

    [Fact]
    public void SessionPeriodIsLastSessionAndRunsWhileLive()
    {
        var book = Open();
        book.Add(At(-60, StatKind.SessionStart));
        book.Add(At(-55, StatKind.Kill, "Волк"));
        book.Add(At(-50, StatKind.SessionEnd));
        book.Add(At(-20, StatKind.SessionStart));
        book.Add(At(-10, StatKind.Kill, "Лиса"));

        var live = book.Summary(StatsPeriod.Session, Now);
        Assert.True(book.Live);
        Assert.Equal(TimeSpan.FromMinutes(20), live.Worked);
        Assert.Equal(1, live.Kills);

        book.Add(At(-5, StatKind.SessionEnd));
        Assert.False(book.Live);
        Assert.Equal(TimeSpan.FromMinutes(15), book.Summary(StatsPeriod.Session, Now).Worked);
        Assert.Equal(2, book.Summary(StatsPeriod.Day, Now).Kills);
    }

    [Fact]
    public void LoadInBackgroundDoesNotDoubleNewEvents()
    {
        var first = Open();
        first.Add(At(-60, StatKind.SessionStart));
        first.Add(At(-50, StatKind.Kill, "Волк"));

        // Новая книга (программу открыли снова) уже пишет, а старое дочитывает потом
        var book = new StatsBook(_directory, Now.ToLocalTime());
        book.Add(At(-40, StatKind.SessionStart));
        book.Add(At(-30, StatKind.Kill, "Лиса"));
        book.Load();

        var day = book.Summary(StatsPeriod.Day, Now);
        Assert.Equal(2, day.Kills);
        Assert.Equal(2, day.Sessions);
    }

    [Fact]
    public void BrokenLinesAreSkipped()
    {
        var book = Open();
        book.Add(At(-60, StatKind.Kill, "Волк"));
        File.AppendAllText(Path.Combine(_directory, "2026-11.jsonl"), "{\"t\":\"2026-11-18T\n{\"k\":\"fromFuture\",\"t\":\"2026-11-18T10:00:00Z\"}\n");
        book.Add(At(-50, StatKind.Kill, "Лиса"));

        Assert.Equal(2, Open().Summary(StatsPeriod.Month, Now).Kills);
    }

    [Fact]
    public void WeekReadsPreviousMonthFile()
    {
        // 2 декабря 2026 — среда: неделя с 30 ноября
        var december = new DateTime(2026, 12, 2, 12, 0, 0, DateTimeKind.Local);
        var old = new StatsBook(_directory, december);
        old.Add(new StatEvent(new DateTime(2026, 11, 30, 10, 0, 0, DateTimeKind.Local).ToUniversalTime(), StatKind.Kill, "Волк"));
        old.Add(new StatEvent(new DateTime(2026, 11, 20, 10, 0, 0, DateTimeKind.Local).ToUniversalTime(), StatKind.Kill, "Лиса"));

        var book = new StatsBook(_directory, december);
        book.Load();

        Assert.Equal(1, book.Summary(StatsPeriod.Week, december.ToUniversalTime()).Kills);
        Assert.Equal(0, book.Summary(StatsPeriod.Month, december.ToUniversalTime()).Kills);
    }

    [Fact]
    public void DirectoryIsPerServerAndNick()
        => Assert.Equal(Path.Combine("root", "stats", "comeback146", "Ник_"), StatsBook.DirectoryFor("root", "comeback146", "Ник?"));
}

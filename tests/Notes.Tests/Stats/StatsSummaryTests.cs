using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Stats;
using Xunit;

namespace Notes.Tests.Stats;

public class StatsSummaryTests
{
    private static readonly DateTime T0 = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    private static StatEvent At(double minutes, StatKind kind, string name = "", long amount = 1)
        => new(T0.AddMinutes(minutes), kind, name, amount);

    [Fact]
    public void CountsAndSumsInsidePeriod()
    {
        List<StatEvent> events =
        [
            At(0, StatKind.SessionStart),
            At(1, StatKind.Kill, "Волк"),
            At(1, StatKind.Exp, amount: 120),
            At(2, StatKind.Item, "Мягкий мех", 2),
            At(3, StatKind.Item, "Уголь"),
            At(4, StatKind.Item, "Мягкий мех", 3),
            At(5, StatKind.Money, amount: 22),
            At(6, StatKind.Death),
            At(7, StatKind.Repair),
            At(8, StatKind.Level, amount: 33),
            At(60, StatKind.SessionEnd),
        ];

        var s = StatsSummary.Of(events, T0, T0.AddDays(1));

        Assert.Equal(TimeSpan.FromHours(1), s.Worked);
        Assert.Equal(1, s.Sessions);
        Assert.Equal(1, s.Kills);
        Assert.Equal(120, s.Exp);
        Assert.Equal(22, s.Money);
        Assert.Equal((1, 1, 1), (s.Deaths, s.Repairs, s.Levels));
        Assert.Equal([new ItemCount("Мягкий мех", 5), new ItemCount("Уголь", 1)], s.Items);
        Assert.Equal(1.0, s.PerHour(s.Kills));
    }

    [Fact]
    public void WorkTimeIsClippedToPeriod()
    {
        // Сессия через полночь: 23:30 — 00:30
        List<StatEvent> events = [At(-30, StatKind.SessionStart), At(30, StatKind.SessionEnd)];

        Assert.Equal(TimeSpan.FromMinutes(30), StatsSummary.Of(events, T0, T0.AddDays(1)).Worked);
        Assert.Equal(TimeSpan.FromMinutes(30), StatsSummary.Of(events, T0.AddDays(-1), T0).Worked);
    }

    [Fact]
    public void SessionWithoutEndEndsAtItsLastEventOrNowIfLive()
    {
        List<StatEvent> events = [At(0, StatKind.SessionStart), At(20, StatKind.Alive), At(30, StatKind.SessionStart), At(35, StatKind.Kill)];

        Assert.Equal(TimeSpan.FromMinutes(25), StatsSummary.Of(events, T0, DateTime.MaxValue).Worked);
        Assert.Equal(TimeSpan.FromMinutes(20 + 15), StatsSummary.Of(events, T0, DateTime.MaxValue, liveNow: T0.AddMinutes(45)).Worked);
        Assert.Equal(2, StatsSummary.Of(events, T0, DateTime.MaxValue).Sessions);
    }

    [Fact]
    public void DrainsWithStartAndDuration()
    {
        List<StatEvent> events =
        [
            At(0, StatKind.SessionStart),
            At(10, StatKind.DrainStart), At(25, StatKind.DrainEnd),
            At(40, StatKind.DrainStart), At(50, StatKind.SessionEnd),
            At(60, StatKind.SessionStart), At(61, StatKind.DrainStart),
        ];

        var drains = StatsSummary.Of(events, T0, DateTime.MaxValue, liveNow: T0.AddMinutes(70)).Drains;

        Assert.Equal(
            [(T0.AddMinutes(10), 15.0, false), (T0.AddMinutes(40), 10.0, false), (T0.AddMinutes(61), 9.0, true)],
            drains.Select(d => (d.Start, d.Duration.TotalMinutes, d.Running)));
    }

    [Fact]
    public void PeriodStartsAtMidnightMondayAndFirstDay()
    {
        var thursday = new DateTime(2026, 11, 5, 15, 20, 0);

        Assert.Equal(new DateTime(2026, 11, 5), StatsSummary.PeriodStart(StatsPeriod.Day, thursday));
        Assert.Equal(new DateTime(2026, 11, 2), StatsSummary.PeriodStart(StatsPeriod.Week, thursday));
        Assert.Equal(new DateTime(2026, 11, 1), StatsSummary.PeriodStart(StatsPeriod.Month, thursday));
        Assert.Equal(new DateTime(2026, 11, 2), StatsSummary.PeriodStart(StatsPeriod.Week, new DateTime(2026, 11, 8, 23, 0, 0)));
        Assert.Null(StatsSummary.PeriodStart(StatsPeriod.Session, thursday));
    }
}

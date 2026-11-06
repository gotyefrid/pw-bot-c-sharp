using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.Stats;

/// <summary>За какое время итоги.</summary>
public enum StatsPeriod
{
    /// <summary>Последняя сессия: от «Старт» до «Стоп» (идёт — до сейчас).</summary>
    Session,
    /// <summary>С полуночи.</summary>
    Day,
    /// <summary>С понедельника.</summary>
    Week,
    /// <summary>С первого числа.</summary>
    Month,
}

/// <summary>Предмет и сколько штук.</summary>
public sealed record ItemCount(string Name, long Count);

/// <summary>Слив опыта: когда начался (всемирное время) и сколько шёл; <see cref="Running"/> — идёт сейчас.</summary>
public sealed record DrainRun(DateTime Start, TimeSpan Duration, bool Running);

/// <summary>Итоги за период (<see cref="Of"/>).</summary>
public sealed record StatsSummary(
    TimeSpan Worked,
    int Sessions,
    int Kills,
    int Deaths,
    long Exp,
    int Levels,
    long Money,
    int Repairs,
    IReadOnlyList<ItemCount> Items,
    IReadOnlyList<DrainRun> Drains)
{
    public static readonly StatsSummary Empty = new(TimeSpan.Zero, 0, 0, 0, 0, 0, 0, 0, [], []);

    /// <summary>Сколько в час работы; работы меньше минуты — null (выйдет ерунда).</summary>
    public double? PerHour(long count) => Worked.TotalMinutes < 1 ? null : count / Worked.TotalHours;

    /// <summary>
    /// Итоги событий за [<paramref name="from"/>, <paramref name="to"/>) — всемирное время. Время работы — куски сессий внутри
    /// периода. Сессия без конца (программу закрыли без «Стоп») кончилась последним своим событием; последняя — идёт, если
    /// задано <paramref name="liveNow"/>: тогда до него.
    /// </summary>
    /// <param name="events">События по времени.</param>
    public static StatsSummary Of(IReadOnlyList<StatEvent> events, DateTime from, DateTime to, DateTime? liveNow = null)
    {
        var sessions = SessionSpans(events, liveNow);
        var inside = events.Where(e => e.Time >= from && e.Time < to).ToList();
        var worked = sessions.Aggregate(TimeSpan.Zero, (sum, s) => sum + Overlap(s.Start, s.End, from, to));

        long Sum(StatKind kind) => inside.Where(e => e.Kind == kind).Sum(e => e.Amount);
        int Count(StatKind kind) => inside.Count(e => e.Kind == kind);

        return new StatsSummary(
            worked,
            sessions.Count(s => s.Start < to && s.End > from || s.Start >= from && s.Start < to),
            Count(StatKind.Kill),
            Count(StatKind.Death),
            Sum(StatKind.Exp),
            Count(StatKind.Level),
            Sum(StatKind.Money),
            Count(StatKind.Repair),
            inside.Where(e => e.Kind == StatKind.Item)
                .GroupBy(e => e.Name)
                .Select(g => new ItemCount(g.Key, g.Sum(e => e.Amount)))
                .OrderByDescending(i => i.Count).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            DrainRuns(events, liveNow).Where(d => d.Start >= from && d.Start < to).ToList());
    }

    /// <summary>Сессии: начало и конец (всемирное время).</summary>
    public static IReadOnlyList<(DateTime Start, DateTime End)> SessionSpans(IReadOnlyList<StatEvent> events, DateTime? liveNow = null)
    {
        var result = new List<(DateTime, DateTime)>();
        DateTime? start = null;
        var seen = DateTime.MinValue;
        foreach (var e in events)
        {
            if (e.Kind == StatKind.SessionStart)
            {
                if (start is { } open)
                    result.Add((open, seen));
                start = seen = e.Time;
            }
            else if (start is { } open)
            {
                seen = e.Time;
                if (e.Kind == StatKind.SessionEnd)
                {
                    result.Add((open, seen));
                    start = null;
                }
            }
        }

        if (start is { } last)
            result.Add((last, liveNow is { } now && now > seen ? now : seen));
        return result;
    }

    // Сливы опыта: начало — конец; конца нет — до конца сессии (идёт — до сейчас)
    private static IEnumerable<DrainRun> DrainRuns(IReadOnlyList<StatEvent> events, DateTime? liveNow)
    {
        DateTime? start = null;
        var seen = DateTime.MinValue;
        foreach (var e in events)
        {
            if (start is { } open && e.Kind is StatKind.DrainStart or StatKind.SessionStart)
            {
                yield return new DrainRun(open, seen - open, false);
                start = null;
            }

            seen = e.Time;
            if (e.Kind == StatKind.DrainStart)
                start = e.Time;
            else if (start is { } running && e.Kind is StatKind.DrainEnd or StatKind.SessionEnd)
            {
                yield return new DrainRun(running, e.Time - running, false);
                start = null;
            }
        }

        if (start is { } last)
            yield return liveNow is { } now ? new DrainRun(last, now - last, true) : new DrainRun(last, seen - last, false);
    }

    private static TimeSpan Overlap(DateTime start, DateTime end, DateTime from, DateTime to)
    {
        var a = start > from ? start : from;
        var b = end < to ? end : to;
        return b > a ? b - a : TimeSpan.Zero;
    }

    /// <summary>
    /// Начало периода по местному времени (<paramref name="nowLocal"/>): полночь, понедельник, первое число. Для сессии — null
    /// (её начало знает только список событий).
    /// </summary>
    public static DateTime? PeriodStart(StatsPeriod period, DateTime nowLocal) => period switch
    {
        StatsPeriod.Day => nowLocal.Date,
        StatsPeriod.Week => nowLocal.Date.AddDays(-(((int)nowLocal.DayOfWeek + 6) % 7)),
        StatsPeriod.Month => new DateTime(nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, nowLocal.Kind),
        _ => null,
    };
}

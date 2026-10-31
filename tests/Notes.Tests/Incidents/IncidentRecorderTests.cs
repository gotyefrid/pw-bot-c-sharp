using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Incidents;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Incidents;

/// <summary>Запись мира вокруг смерти: минута до события, минута после, ничего не теряется и не затирается.</summary>
public class IncidentRecorderTests
{
    private readonly FakeWorld _world = new();
    private readonly IncidentRecorder _recorder = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    private readonly List<Incident> _closed = [];

    public IncidentRecorderTests() => _recorder.Closed += _closed.Add;

    // Снимки раз в 250 мс, как в боте
    private WorldState Tick(int count = 1)
    {
        WorldState last = null!;
        for (var i = 0; i < count; i++)
        {
            last = _world.Wait(0.25).Snapshot();
            _recorder.Observe(last);
        }

        return last;
    }

    [Fact]
    public void NothingHappenedNothingWritten()
    {
        Tick(200);

        Assert.Empty(_closed);
    }

    [Fact]
    public void KeepsOnlyLastMinuteBeforeAndRecordsAfter()
    {
        Tick(100);
        var death = Tick();
        _recorder.Mark(death, "Вас убил Моб");
        Tick(19);
        Assert.Empty(_closed);

        Tick();

        var incident = Assert.Single(_closed);
        Assert.Equal("Вас убил Моб", Assert.Single(incident.Events).Text);
        Assert.Same(death, incident.Events[0].World);
        Assert.Equal(TimeSpan.FromSeconds(10), death.Time - incident.Start);
        Assert.Equal(TimeSpan.FromSeconds(5), incident.End - death.Time);
        // Без пропусков и повторов: каждый снимок по разу, по порядку
        var times = incident.Snapshots.Select(s => s.Time).ToList();
        Assert.Equal(41 + 20, times.Count);
        Assert.Equal(times.OrderBy(t => t), times);
        Assert.Equal(times.Count, times.Distinct().Count());
    }

    [Fact]
    public void SecondEventExtendsSameIncident()
    {
        Tick(10);
        _recorder.Mark(Tick(), "Пет погиб");
        Tick(12);
        _recorder.Mark(Tick(), "Вас убил Моб");
        Tick(19);
        Assert.Empty(_closed);

        Tick();

        var incident = Assert.Single(_closed);
        Assert.Equal(["Пет погиб", "Вас убил Моб"], incident.Events.Select(e => e.Text));
        Assert.Equal(TimeSpan.FromSeconds(5), incident.End - incident.Events[1].Time);
    }

    [Fact]
    public void NotLongerThanMaxLength()
    {
        Tick(40);
        for (var i = 0; i < 10; i++)
        {
            _recorder.Mark(Tick(), $"событие {i}");
            Tick(15);
        }

        var incident = _closed.First();
        Assert.True(incident.End - incident.Start <= TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void HpZeroIsDeathEvenWithoutChat()
    {
        Tick(4);
        _world.Hp = 0;
        var death = Tick();
        Tick(20);

        var incident = Assert.Single(_closed);
        Assert.Equal("HP персонажа упало до 0", incident.Events[0].Text);
        Assert.Same(death, incident.Events[0].World);
    }

    [Fact]
    public void LyingDeadIsOneDeath()
    {
        _world.Hp = 0;
        Tick(100);

        Assert.Empty(_closed);
    }

    [Fact]
    public void NewIncidentAfterPreviousClosed()
    {
        Tick(4);
        _recorder.Mark(Tick(), "первое");
        Tick(50);
        _recorder.Mark(Tick(), "второе");
        Tick(20);

        Assert.Equal(["первое", "второе"], _closed.Select(i => i.Events.Single().Text));
        // У второго — свои 10 с до события, хотя часть из них была «после» первого
        Assert.Equal(TimeSpan.FromSeconds(10), _closed[1].Events[0].Time - _closed[1].Start);
    }

    private void Log(string text) => _recorder.Write(new LogEntry(_world.Time.ToLocalTime(), LogLevel.Info, LogSources.Brain, text));

    [Fact]
    public void BotLogOfSameMinute()
    {
        Log("давно");
        Tick(60);
        Log("бью Летягу");
        Tick(4);
        _recorder.Mark(Tick(), "Вас убил Летяга");
        Tick(2);
        Log("встаю");
        Tick(20);

        Assert.Equal(["бью Летягу", "встаю"], Assert.Single(_closed).Log.Select(e => e.Message));
    }

    [Fact]
    public void FlushForgetsHistory()
    {
        Tick(20);
        Log("с прошлым клиентом");
        _recorder.Flush();
        _recorder.Mark(Tick(), "Вас убил Моб");
        _recorder.Flush();

        var incident = Assert.Single(_closed);
        Assert.Single(incident.Snapshots);
        Assert.Empty(incident.Log);
    }

    [Fact]
    public void FlushGivesWhatWasRecorded()
    {
        Tick(4);
        _recorder.Mark(Tick(), "Вас убил Моб");
        Tick(2);

        _recorder.Flush();

        Assert.Equal(7, Assert.Single(_closed).Snapshots.Count);
        _recorder.Flush();
        Assert.Single(_closed);
    }
}

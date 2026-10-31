using System;
using System.IO;
using System.Linq;
using Notes.Core.Incidents;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Incidents;

/// <summary>Файл случая: что случилось и всё, что бот знал о мире в момент события.</summary>
public class IncidentReportTests
{
    private const uint Bat = 0x8010_0001;
    private const uint Itori = 0x0012_0001;
    private readonly FakeWorld _world = new();

    private Incident Death(string text = "Вас убил Летяга-разбойница", double seconds = 0)
    {
        var w = _world.Wait(seconds).Snapshot();
        return new Incident([new IncidentEvent(w.Time, text, w)], [w]);
    }

    [Fact]
    public void MomentHasEveryoneAround()
    {
        _world.Hp = 0;
        _world.TargetWid = Bat;
        _world.AddMob(Bat, "Летяга-разбойница", 3, FakeWorld.HostWid, hp: 1200);
        _world.Players.Add(new PlayerInfo(2, Itori, "Itori", new Position(25, 0, 0)) { Class = PlayerClasses.Assassin, TargetWid = FakeWorld.HostWid });
        _world.Ground.Add(new GroundItem(3, 7, 100, GroundItemKind.Resource, new Position(0, 0, 8), "Медная руда"));
        _world.Attacks.Add(new AttackEvent(4, 300, Bat, 0, [new AttackHit(FakeWorld.HostWid, 581, 0)]));
        _world.SetPet(2, hpRatio: 0, summoned: false);

        var text = IncidentReport.Text(Death());

        Assert.Contains("Вас убил Летяга-разбойница", text);
        Assert.Contains("Персонаж Перс: ур. 10, HP 0/500, MP 300/500", text);
        Assert.Contains("цель: Летяга-разбойница", text);
        Assert.Contains("Летяга-разбойница, бьёт, HP 1200, цель → вас", text);
        Assert.Contains("Itori, убийца, цель → вас", text);
        Assert.Contains("Медная руда (ресурс)", text);
        Assert.Contains("Летяга-разбойница → вы: 581", text);
        Assert.Contains("Пет: не призван", text);
    }

    [Fact]
    public void ChronicleHasStartChangesAndEvent()
    {
        var start = _world.Snapshot();
        _world.AddMob(Bat, "Летяга-разбойница", 3, FakeWorld.HostWid);
        var death = _world.Wait(0.25).Snapshot();

        var log = new LogEntry(start.Time.AddSeconds(0.1).ToLocalTime(), LogLevel.Info, LogSources.Brain, "бью Летягу-разбойницу");
        var text = IncidentReport.Text(new Incident([new IncidentEvent(death.Time, "Вас убил Летяга-разбойница", death)], [start, death]) { Log = [log] });

        var chronicle = text.Substring(text.IndexOf("=== Хроника: что менялось", StringComparison.Ordinal));
        Assert.Contains($"{IncidentReport.Clock(death.Time)}  появился моб Летяга-разбойница, 3,0 м, цель → вас", chronicle);
        Assert.Contains($"{IncidentReport.Clock(death.Time)}  ☠ Вас убил Летяга-разбойница", chronicle);
        // Строка бота — между снимками, по своему времени
        Assert.StartsWith($"{IncidentReport.Clock(log.Time)}  бот [мозг]: бью Летягу-разбойницу", chronicle.Split('\n')[1]);
        Assert.Contains("=== Хроника: в начале записи", text);
    }

    [Fact]
    public void ChatOnlyLastLines()
    {
        var w = _world.Snapshot() with { Chat = Enumerable.Range(1, 200).Select(n => new ChatMessage(n, 1, $"сообщение {n}", 1)).ToList() };
        var text = IncidentReport.Text(new Incident([new IncidentEvent(w.Time, "Вас убил Моб", w)], [w]));

        Assert.Contains("Чат (последние 40 из 200)", text);
        Assert.Contains("№200 [1] сообщение 200", text);
        Assert.DoesNotContain("№160 ", text);
        Assert.Contains("№161 ", text);
    }

    [Fact]
    public void FilesKeepOnlyLast()
    {
        var directory = Path.Combine(Path.GetTempPath(), "notes-incidents-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new IncidentFiles(directory, keep: 3);
            var paths = Enumerable.Range(0, 5).Select(_ => files.Write(Death(seconds: 1))).ToList();

            Assert.Equal(paths.Count, paths.Distinct().Count());
            Assert.Equal(3, Directory.GetFiles(directory).Length);
            Assert.All(paths.Skip(2), p => Assert.True(File.Exists(p)));
            Assert.Contains("Вас убил Летяга-разбойница", File.ReadAllText(paths[4]));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void SameSecondGetsOwnFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "notes-incidents-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = new IncidentFiles(directory);
            var first = files.Write(Death());
            var second = files.Write(Death());

            Assert.EndsWith(" (2).txt", second);
            Assert.True(File.Exists(first));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}

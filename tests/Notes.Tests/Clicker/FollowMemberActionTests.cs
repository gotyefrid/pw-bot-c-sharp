using System.Linq;
using Notes.Core.Clicker;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Пати: Следовать»: по нику из пати в снимке. Ведёт игра — итог не ждёт; ведёт бот — идёт туда, где член пати, до
/// «остановиться ближе», за отошедшим — к новому месту. Не в пати, его нет в пати, выбран сам персонаж — «не вышло».
/// </summary>
public class FollowMemberActionTests
{
    private const uint MateWid = 0x130ECE0;

    private readonly ClickerScript _script = new();

    private string[] WithKey(FollowMemberAction action) => [.. _script.Step(action).Key("F1").Run(_script.Start(), 1)];

    private void InParty(Position? mateAt = null) => _script.World.Party = new PartyInfo(FakeWorld.HostWid,
        [Mate(FakeWorld.HostWid, "Перс", null), Mate(MateWid, "Друг", mateAt)]);

    private static PartyMember Mate(uint wid, string name, Position? at) => new(wid, name, 50, 100, 100, 100, 100, at);

    private static FollowMemberAction BotLeads(FollowWay way = FollowWay.Direct) => new() { Member = "Друг", Way = way, StopDistance = 3 };

    [Fact]
    public void FollowsMateByNameAndGoesOn()
    {
        InParty();

        Assert.Equal([$"follow {MateWid:X}, key F1"], WithKey(new FollowMemberAction { Member = "друг" }));
    }

    [Fact]
    public void NotInPartyIsReported()
    {
        Assert.Equal(["key F1"], WithKey(new FollowMemberAction { Member = "Друг" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("персонаж не в пати"));
    }

    [Fact]
    public void MateNotInPartyIsReported()
    {
        InParty();

        Assert.Equal(["key F1"], WithKey(new FollowMemberAction { Member = "Чужой" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("«Чужой» нет в пати"));
    }

    [Fact]
    public void SelfIsReported()
    {
        InParty();

        Assert.Equal(["key F1"], WithKey(new FollowMemberAction { Member = "Перс" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("это сам персонаж"));
    }

    [Fact]
    public void BotWalksToMateThenGoesOn()
    {
        InParty(new Position(20, 0, 0));
        var bot = _script.Step(BotLeads()).Key("F1").Start();

        var first = _script.Run(bot, 1).Single();
        Assert.StartsWith("move", first);
        Assert.DoesNotContain("key", first);
        Assert.Equal(new Position(20, 0, 0), _script.Game.MovePoints.Single());

        // Дошли до 3 м — дальше следующий шаг
        _script.World.Position = new Position(18, 0, 0);
        Assert.Contains(_script.Run(bot, 2), turn => turn.Contains("key F1"));
    }

    [Fact]
    public void SmartWayGoesAroundObstacles()
    {
        InParty(new Position(20, 0, 0));

        Assert.EndsWith("умно", WithKey(BotLeads(FollowWay.Smart)).Single());
    }

    [Fact]
    public void AlreadyNearSendsNothing()
    {
        InParty(new Position(2, 0, 0));

        Assert.Equal(["key F1"], WithKey(BotLeads()));
    }

    [Fact]
    public void MateMovedAwayIsFollowedToNewPlace()
    {
        InParty(new Position(20, 0, 0));
        var bot = _script.Step(BotLeads()).Key("F1").Start();
        _script.Run(bot, 1);

        InParty(new Position(40, 0, 0));
        for (var i = 1; i <= 5; i++)
        {
            _script.World.Position = new Position(i, 0, 0);
            _script.Run(bot, 1);
        }

        Assert.Equal([new Position(20, 0, 0), new Position(40, 0, 0)], _script.Game.MovePoints);
    }

    [Fact]
    public void FliesWhenInAir()
    {
        InParty(new Position(20, 10, 0));
        _script.World.Flying = true;

        Assert.StartsWith("fly", WithKey(BotLeads()).Single());
        Assert.Equal(new Position(20, 10, 0), _script.Game.FlyPoints.Single());
    }

    [Fact]
    public void UnknownWhereMateIsIsReported()
    {
        InParty();

        Assert.Equal(["key F1"], WithKey(BotLeads()));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("игра не знает"));
    }

    [Fact]
    public void FileKeepsWayAndStop()
    {
        var back = ClickerScript.RoundTrip(new FollowMemberAction { Member = "Друг", Way = FollowWay.Smart, StopDistance = 7 });
        Assert.Equal(FollowWay.Smart, back.Way);
        Assert.Equal(7, back.StopDistance);
        Assert.Equal(FollowMemberAction.MaxStopDistance, ClickerScript.RoundTrip(new FollowMemberAction { StopDistance = 999 }).StopDistance);
        Assert.Equal(1, ClickerScript.RoundTrip(new FollowMemberAction { StopDistance = 0 }).StopDistance);
        Assert.DoesNotContain("\"way\"", ClickerScript.Json(new FollowMemberAction { Member = "Друг" }));
    }

    [Fact]
    public void NotChosenIsProblem() => Assert.Equal("член пати не выбран", new FollowMemberAction().Problem);

    [Fact]
    public void FileKeepsMember() => Assert.Equal("Друг", ClickerScript.RoundTrip(new FollowMemberAction { Member = " Друг " }).Member);

    [Fact]
    public void Described()
    {
        Assert.Equal("член пати не выбран", new FollowMemberAction().Describe());
        Assert.Equal("следовать за Друг", new FollowMemberAction { Member = "Друг" }.Describe());
        Assert.Equal("идти к Друг по прямой, до 3 м", BotLeads().Describe());
        Assert.Equal("идти к Друг в обход препятствий, до 3 м", BotLeads(FollowWay.Smart).Describe());
    }
}

using Notes.Core.Clicker;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Пати: Следовать»: по нику из пати в снимке, ведёт игра — итог не ждёт. Не в пати, его нет в пати, выбран сам персонаж —
/// «не вышло».
/// </summary>
public class FollowMemberActionTests
{
    private const uint MateWid = 0x130ECE0;

    private readonly ClickerScript _script = new();

    private string[] WithKey(FollowMemberAction action) => [.. _script.Step(action).Key("F1").Run(_script.Start(), 1)];

    private void InParty() => _script.World.Party = new PartyInfo(FakeWorld.HostWid,
        [Mate(FakeWorld.HostWid, "Перс"), Mate(MateWid, "Друг")]);

    private static PartyMember Mate(uint wid, string name) => new(wid, name, 50, 100, 100, 100, 100, null);

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
    public void NotChosenIsProblem() => Assert.Equal("член пати не выбран", new FollowMemberAction().Problem);

    [Fact]
    public void FileKeepsMember() => Assert.Equal("Друг", ClickerScript.RoundTrip(new FollowMemberAction { Member = " Друг " }).Member);

    [Fact]
    public void Described()
    {
        Assert.Equal("член пати не выбран", new FollowMemberAction().Describe());
        Assert.Equal("следовать за Друг", new FollowMemberAction { Member = "Друг" }.Describe());
    }
}

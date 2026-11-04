using Notes.Core.Clicker;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Пати: Ассист»: по нику из пати в снимке, итог не ждёт. Не в пати, его нет в пати, выбран сам персонаж —
/// «не вышло».
/// </summary>
public class AssistActionTests
{
    private const uint MateWid = 0x130ECE0;

    private readonly ClickerScript _script = new();

    private string[] WithKey(AssistAction action) => [.. _script.Step(action).Key("F1").Run(_script.Start(), 1)];

    private void InParty() => _script.World.Party = new PartyInfo(FakeWorld.HostWid,
        [Mate(FakeWorld.HostWid, "Перс"), Mate(MateWid, "Друг")]);

    private static PartyMember Mate(uint wid, string name) => new(wid, name, 50, 100, 100, 100, 100, null);

    [Fact]
    public void AssistsMateByNameAndGoesOn()
    {
        InParty();

        Assert.Equal([$"assist {MateWid:X}, key F1"], WithKey(new AssistAction { Member = "друг" }));
    }

    [Fact]
    public void MateWithoutTargetSendsNothing()
    {
        InParty();
        MateSeen(target: uint.MaxValue);

        Assert.Equal(["key F1"], WithKey(new AssistAction { Member = "Друг" }));
        Assert.Contains(_script.Log, e => e.Message.Contains("у Друг нет цели"));
        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public void MateWithSameTargetSendsNothing()
    {
        InParty();
        MateSeen(target: 0x80001);
        _script.World.TargetWid = 0x80001;

        Assert.Equal(["key F1"], WithKey(new AssistAction { Member = "Друг" }));
        Assert.Contains(_script.Log, e => e.Message.Contains("у Друг та же цель"));
    }

    [Fact]
    public void MateTargetUnknownIsAssisted()
    {
        InParty();
        MateSeen(target: 0);

        Assert.Equal([$"assist {MateWid:X}, key F1"], WithKey(new AssistAction { Member = "Друг" }));
    }

    private void MateSeen(uint target)
        => _script.World.Players.Add(new PlayerInfo(1, MateWid, "Друг", new Position(3, 0, 0)) { TargetWid = target });

    [Fact]
    public void MateWithOtherTargetIsAssisted()
    {
        InParty();
        MateSeen(target: 0x80001);

        Assert.Equal([$"assist {MateWid:X}, key F1"], WithKey(new AssistAction { Member = "Друг" }));
    }

    [Fact]
    public void NotInPartyIsReported()
    {
        Assert.Equal(["key F1"], WithKey(new AssistAction { Member = "Друг" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("персонаж не в пати"));
    }

    [Fact]
    public void MateNotInPartyIsReported()
    {
        InParty();

        Assert.Equal(["key F1"], WithKey(new AssistAction { Member = "Чужой" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("«Чужой» нет в пати"));
    }

    [Fact]
    public void SelfIsReported()
    {
        InParty();

        Assert.Equal(["key F1"], WithKey(new AssistAction { Member = "Перс" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("это сам персонаж"));
    }

    [Fact]
    public void NotChosenIsProblem() => Assert.Equal("член пати не выбран", new AssistAction().Problem);

    [Fact]
    public void FileKeepsMember() => Assert.Equal("Друг", ClickerScript.RoundTrip(new AssistAction { Member = " Друг " }).Member);

    [Fact]
    public void Described()
    {
        Assert.Equal("член пати не выбран", new AssistAction().Describe());
        Assert.Equal("ассист Друг", new AssistAction { Member = "Друг" }.Describe());
    }
}

using Notes.Core.Clicker;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Пати: в цель»: выбранного по нику или самого раненого (и сам персонаж в счёт), со своим условием по HP или без.
/// Мёртвых не берёт; подходящий уже в цели — ничего не шлёт; некого — дальше без предупреждения; не в пати или выбранного
/// нет — «не вышло».
/// </summary>
public class PartyTargetActionTests
{
    private const uint FriendWid = 2;
    private const uint TankWid = 3;

    private readonly ClickerScript _script = new();

    private string[] WithKey(PartyTargetAction action) => [.. _script.Step(action).Key("F1").Run(_script.Start(), 1)];

    // Перс: 400/500 = 80 %
    private void Party(int friendHp, int tankHp = 1000)
        => _script.World.Party = new PartyInfo(FakeWorld.HostWid,
        [
            new PartyMember(FakeWorld.HostWid, "Перс", 50, 1, 1, 0, 0, null),
            new PartyMember(FriendWid, "Друг", 50, friendHp, 1000, 0, 0, null),
            new PartyMember(TankWid, "Танк", 50, tankHp, 1000, 0, 0, null),
        ]);

    [Fact]
    public void MostWoundedBelowThreshold()
    {
        Party(friendHp: 400, tankHp: 300);

        Assert.Equal([$"select {TankWid:X}, key F1"], WithKey(new PartyTargetAction()));
    }

    [Fact]
    public void SelfCanBeMostWounded()
    {
        Party(friendHp: 900);
        _script.World.Hp = 100;

        Assert.Equal([$"select {FakeWorld.HostWid:X}, key F1"], WithKey(new PartyTargetAction()));
    }

    [Fact]
    public void NobodyFitsIsSkippedWithoutWarning()
    {
        Party(friendHp: 900);

        Assert.Equal(["key F1"], WithKey(new PartyTargetAction()));
        Assert.Contains(_script.Log, e => e.Message.Contains("в пати некого брать в цель"));
        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public void WithoutHpCheckMostWoundedAlways()
    {
        // Все выше порога, а без условия всё равно берёт самого раненого: перс 80 % — ниже Друга (90 %) и Танка (95 %)
        Party(friendHp: 900, tankHp: 950);

        Assert.Equal([$"select {FakeWorld.HostWid:X}, key F1"], WithKey(new PartyTargetAction { ByHp = false }));
    }

    [Fact]
    public void ChosenMemberByHp()
    {
        Party(friendHp: 300, tankHp: 100);

        Assert.Equal([$"select {FriendWid:X}, key F1"], WithKey(new PartyTargetAction { Member = "друг" }));
    }

    [Fact]
    public void ChosenMemberAboveThresholdIsSkipped()
    {
        Party(friendHp: 900);

        Assert.Equal(["key F1"], WithKey(new PartyTargetAction { Member = "Друг" }));
    }

    [Fact]
    public void ChosenMemberWithoutHpCheckAlways()
    {
        Party(friendHp: 1000);

        Assert.Equal([$"select {FriendWid:X}, key F1"], WithKey(new PartyTargetAction { Member = "Друг", ByHp = false }));
    }

    [Fact]
    public void ComparesAbove()
    {
        Party(friendHp: 900, tankHp: 700);

        Assert.Equal([$"select {TankWid:X}, key F1"],
            WithKey(new PartyTargetAction { Compare = PercentCompare.Above, Percent = 60 }));
    }

    [Fact]
    public void DeadAreNotTaken()
    {
        Party(friendHp: 0, tankHp: 300);

        Assert.Equal([$"select {TankWid:X}, key F1"], WithKey(new PartyTargetAction()));
    }

    [Fact]
    public void AlreadyInTargetSendsNothing()
    {
        Party(friendHp: 300);
        _script.World.TargetWid = FriendWid;

        Assert.Equal(["key F1"], WithKey(new PartyTargetAction()));
        Assert.Contains(_script.Log, e => e.Message.Contains("«Друг» уже в цели"));
    }

    [Fact]
    public void WaitsUntilInTargetWhenAsked()
    {
        Party(friendHp: 300);
        var bot = _script.Step(new PartyTargetAction { WaitConfirmed = true }).Key("F1").Start();

        Assert.Equal([$"select {FriendWid:X}", ""], _script.Run(bot, 2));

        _script.World.TargetWid = FriendWid;
        Assert.Equal(["key F1"], _script.Run(bot, 1));
    }

    [Fact]
    public void NotInPartyIsReported()
    {
        Assert.Equal(["key F1"], WithKey(new PartyTargetAction()));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("персонаж не в пати"));
    }

    [Fact]
    public void ChosenNotInPartyIsReported()
    {
        Party(friendHp: 300);

        Assert.Equal(["key F1"], WithKey(new PartyTargetAction { Member = "Чужой" }));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("«Чужой» нет в пати"));
    }

    [Fact]
    public void FileKeepsFields()
    {
        var back = ClickerScript.RoundTrip(new PartyTargetAction { Member = " Друг ", ByHp = false, Compare = PercentCompare.Equal, Percent = -5 });

        Assert.Equal("Друг", back.Member);
        Assert.False(back.ByHp);
        Assert.Equal(PercentCompare.Equal, back.Compare);
        Assert.Equal(0, back.Percent);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("в цель самого раненого в пати с HP меньше 50 %", new PartyTargetAction().Describe());
        Assert.Equal("в цель самого раненого в пати", new PartyTargetAction { ByHp = false }.Describe());
        Assert.Equal("в цель Друг, если HP меньше 30 %", new PartyTargetAction { Member = "Друг", Percent = 30 }.Describe());
        Assert.Equal("в цель Друг", new PartyTargetAction { Member = "Друг", ByHp = false }.Describe());
    }
}

using System.Linq;
using Newtonsoft.Json.Linq;
using Notes.Core.Clicker;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Пати: взять в цель»: выбранного по нику — всегда; «любого первого по условию» — первого по порядку пати (и сам
/// персонаж на своём месте), у кого HP подходит. Мёртвых не берёт; подходящий уже в цели — ничего не шлёт; никто не подходит —
/// никого, дальше без предупреждения; не в пати или выбранного нет — «не вышло».
/// </summary>
public class PartyTargetActionTests
{
    private const uint FriendWid = 2;
    private const uint TankWid = 3;

    private readonly ClickerScript _script = new();

    private string[] WithKey(PartyTargetAction action) => [.. _script.Step(action).Key("F1").Run(_script.Start(), 1)];

    // По порядку пати: Перс (400/500 = 80 %), Друг, Танк
    private void Party(int friendHp, int tankHp = 1000)
        => _script.World.Party = new PartyInfo(FakeWorld.HostWid,
        [
            new PartyMember(FakeWorld.HostWid, "Перс", 50, 1, 1, 0, 0, null),
            new PartyMember(FriendWid, "Друг", 50, friendHp, 1000, 0, 0, null),
            new PartyMember(TankWid, "Танк", 50, tankHp, 1000, 0, 0, null),
        ]);

    [Fact]
    public void AnyTakesFirstFittingInPartyOrder()
    {
        // Танк ранен сильнее, но Друг в пати раньше — берётся он
        Party(friendHp: 400, tankHp: 300);

        Assert.Equal([$"select {FriendWid:X}, key F1"], WithKey(new PartyTargetAction()));
    }

    [Fact]
    public void AnyCountsSelfOnItsPlace()
    {
        Party(friendHp: 300);
        _script.World.Hp = 100;

        Assert.Equal([$"select {FakeWorld.HostWid:X}, key F1"], WithKey(new PartyTargetAction()));
    }

    [Fact]
    public void AnyWithoutSelfSkipsSelf()
    {
        // Перс ранен и первый в пати, но себя не брать — следующий подходящий
        Party(friendHp: 900, tankHp: 300);
        _script.World.Hp = 100;

        Assert.Equal([$"select {TankWid:X}, key F1"], WithKey(new PartyTargetAction { IncludeSelf = false }));
    }

    [Fact]
    public void OldFileIncludesSelf()
        => Assert.True(ClickerScript.WithoutField(new PartyTargetAction { IncludeSelf = false }, "includeSelf").IncludeSelf);

    [Fact]
    public void AnyNobodyFitsTakesNobodyWithoutWarning()
    {
        Party(friendHp: 900);

        Assert.Equal(["key F1"], WithKey(new PartyTargetAction()));
        Assert.Contains(_script.Log, e => e.Message.Contains("в пати некого брать в цель"));
        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
    }

    [Fact]
    public void AnyComparesAbove()
    {
        Party(friendHp: 500, tankHp: 700);

        Assert.Equal([$"select {FakeWorld.HostWid:X}, key F1"],
            WithKey(new PartyTargetAction { Compare = PercentCompare.Above, Percent = 60 }));
    }

    [Fact]
    public void ChosenMemberAlwaysWhateverHp()
    {
        // Порог в действии для выбранного не действует — условие по его HP задаётся условием шага
        Party(friendHp: 1000, tankHp: 100);

        Assert.Equal([$"select {FriendWid:X}, key F1"], WithKey(new PartyTargetAction { Member = "друг" }));
    }

    [Fact]
    public void DeadAreNotTaken()
    {
        Party(friendHp: 0, tankHp: 300);

        Assert.Equal([$"select {TankWid:X}, key F1"], WithKey(new PartyTargetAction()));
    }

    [Fact]
    public void ChosenDeadIsNotTaken()
    {
        Party(friendHp: 0);

        Assert.Equal(["key F1"], WithKey(new PartyTargetAction { Member = "Друг" }));
        Assert.Contains(_script.Log, e => e.Message.Contains("«Друг» мёртв"));
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
        var back = ClickerScript.RoundTrip(new PartyTargetAction { Member = " Друг ", Compare = PercentCompare.Equal, Percent = -5, IncludeSelf = false });

        Assert.False(back.IncludeSelf);
        Assert.Equal("Друг", back.Member);
        Assert.Equal(PercentCompare.Equal, back.Compare);
        Assert.Equal(0, back.Percent);
    }

    [Fact]
    public void OldFileWithByHpStillReads()
    {
        // Сценарии до «первого по условию» — с полем byHp: оно больше ничего не значит, файл читается
        var json = JObject.Parse(ClickerScript.Json(new PartyTargetAction { Member = "Друг", Percent = 30 }));
        json.Descendants().OfType<JObject>().First(o => o["member"] is not null)["byHp"] = false;

        var back = (PartyTargetAction)SettingsJson.Parse(json.ToString()).Clicker.Steps.Single().Action;

        Assert.Equal("Друг", back.Member);
        Assert.Equal(30, back.Percent);
    }

    [Fact]
    public void HpRowOnlyForAny()
    {
        Assert.True(new PartyTargetAction().ByHp);
        Assert.False(new PartyTargetAction { Member = "Друг" }.ByHp);
        Assert.Equal(PartyTargetAction.AnyText, new PartyTargetAction().MemberShown);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("в цель первого в пати с HP меньше 50 %", new PartyTargetAction().Describe());
        Assert.Equal("в цель первого в пати (кроме себя) с HP меньше 50 %", new PartyTargetAction { IncludeSelf = false }.Describe());
        Assert.Equal("в цель Друг", new PartyTargetAction { Member = "Друг", Percent = 30 }.Describe());
    }
}

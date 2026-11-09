using Notes.Core.Clicker;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Условие «Пати: HP, %»: член пати по нику или любой (и сам персонаж — его HP из персонажа); меньше, больше, равно. С «любым» —
/// хоть один подходит, в лог — самый раненый из подходящих. Не в пати, выбранного нет, максимум HP неизвестен — не выполнено.
/// </summary>
public class PartyHpConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(PartyHpCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    // Перс: 400/500 = 80 %; Друг и Танк — HP из 1000
    private void Party(int friendHp, int otherHp = 1000, int friendMax = 1000)
        => _world.Party = new PartyInfo(FakeWorld.HostWid,
        [
            new PartyMember(FakeWorld.HostWid, "Перс", 50, 1, 1, 0, 0, null),
            new PartyMember(2, "Друг", 50, friendHp, friendMax, 0, 0, null),
            new PartyMember(3, "Танк", 50, otherHp, 1000, 0, 0, null),
        ]);

    [Fact]
    public void ChosenMemberBelow()
    {
        Party(friendHp: 300);

        Assert.Equal("HP «Друг» 30 % — меньше 50 %", Check(new PartyHpCondition { Member = "друг" }));
        Assert.Null(Check(new PartyHpCondition { Member = "Танк" }));
    }

    [Fact]
    public void AnyIsLowestOfFitting()
    {
        Party(friendHp: 300, otherHp: 200);

        Assert.Equal("HP «Танк» 20 % — меньше 50 %", Check(new PartyHpCondition()));
    }

    [Fact]
    public void AnyIncludesSelfWithHostHp()
    {
        // HP самого персонажа — из персонажа, а не из записи пати (там 1/1)
        Party(friendHp: 1000);
        _world.Hp = 100;

        Assert.Equal("HP «Перс» 20 % — меньше 50 %", Check(new PartyHpCondition()));
    }

    [Fact]
    public void NobodyFitsDoesNotHold()
    {
        Party(friendHp: 900);

        Assert.Null(Check(new PartyHpCondition()));
    }

    [Theory]
    [InlineData(PercentCompare.Below, 490, true)]
    [InlineData(PercentCompare.Below, 500, false)]
    [InlineData(PercentCompare.Above, 510, true)]
    [InlineData(PercentCompare.Above, 500, false)]
    [InlineData(PercentCompare.Equal, 500, true)]
    [InlineData(PercentCompare.Equal, 510, false)]
    public void Compares(PercentCompare compare, int hp, bool holds)
    {
        Party(friendHp: hp);

        Assert.Equal(holds, Check(new PartyHpCondition { Member = "Друг", Compare = compare }) is not null);
    }

    [Fact]
    public void NotInPartyOrNotThereOrUnknownMaxDoesNotHold()
    {
        Assert.Null(Check(new PartyHpCondition()));

        Party(friendHp: 100, friendMax: 0);
        Assert.Null(Check(new PartyHpCondition { Member = "Друг" }));
        Assert.Null(Check(new PartyHpCondition { Member = "Чужой" }));
    }

    [Fact]
    public void FileKeepsFields()
    {
        var back = ClickerScript.RoundTrip(new PartyHpCondition { Member = " Друг ", Compare = PercentCompare.Above, Percent = 500 });

        Assert.Equal("Друг", back.Member);
        Assert.Equal(PercentCompare.Above, back.Compare);
        Assert.Equal(100, back.Percent);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("HP любого в пати меньше 50 %", new PartyHpCondition().Describe());
        Assert.Equal("HP Друг больше 70 %", new PartyHpCondition { Member = "Друг", Compare = PercentCompare.Above, Percent = 70 }.Describe());
    }
}

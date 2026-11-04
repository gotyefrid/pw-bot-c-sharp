using Notes.Core.Clicker;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Условие «Пати: член пати дальше, м»: член пати по нику, расстояние напрямую с высотой; где он, игра не знает — далеко. Не в
/// пати, его нет в пати, выбран сам персонаж — не выполнено.
/// </summary>
public class PartyMemberFarConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(PartyMemberFarCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    private void MateAt(Position? position) => _world.Party = new PartyInfo(FakeWorld.HostWid,
        [new(FakeWorld.HostWid, "Перс", 50, 100, 100, 100, 100, null), new(0x130ECE0, "Друг", 50, 100, 100, 100, 100, position)]);

    [Fact]
    public void FartherThanRadius()
    {
        MateAt(new Position(20, 0, 0));

        Assert.Equal("«Друг» в 20,0 м — дальше 15 м", Check(new PartyMemberFarCondition { Member = "друг" }));
        Assert.Null(Check(new PartyMemberFarCondition { Member = "Друг", Radius = 20 }));
    }

    [Fact]
    public void HeightCounts()
    {
        MateAt(new Position(9, 12, 0));

        Assert.NotNull(Check(new PartyMemberFarCondition { Member = "Друг", Radius = 14 }));
        Assert.Null(Check(new PartyMemberFarCondition { Member = "Друг", Radius = 15 }));
    }

    [Fact]
    public void UnknownPositionIsFar()
    {
        MateAt(null);

        Assert.Equal("где «Друг» — игра не знает, считаем далеко", Check(new PartyMemberFarCondition { Member = "Друг" }));
    }

    [Fact]
    public void NotInPartyOrNotFoundOrSelfDoesNotHold()
    {
        Assert.Null(Check(new PartyMemberFarCondition { Member = "Друг" }));

        MateAt(null);
        Assert.Null(Check(new PartyMemberFarCondition { Member = "Чужой" }));
        Assert.Null(Check(new PartyMemberFarCondition { Member = "Перс" }));
        Assert.Null(Check(new PartyMemberFarCondition()));
    }

    [Fact]
    public void FileKeepsMemberAndCleansRadius()
    {
        var back = ClickerScript.RoundTrip(new PartyMemberFarCondition { Member = " Друг ", Radius = -1 });

        Assert.Equal("Друг", back.Member);
        Assert.Equal(1, back.Radius);
        Assert.Equal(GroundItemChoice.MaxRadius, ClickerScript.RoundTrip(new PartyMemberFarCondition { Radius = 5000 }).Radius);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("член пати не выбран", new PartyMemberFarCondition().Describe());
        Assert.Equal("Друг дальше 15 м", new PartyMemberFarCondition { Member = "Друг" }.Describe());
        Assert.Equal("НЕ (Друг дальше 15 м)", new PartyMemberFarCondition { Member = "Друг", Not = true }.Describe());
    }
}

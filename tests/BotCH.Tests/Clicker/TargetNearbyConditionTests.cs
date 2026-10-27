using System.Linq;
using BotCH.Core.Clicker;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «Рядом можно взять цель»: мобы, NPC, чужие петы по названиям, расстояние от персонажа; свой пет и трупы не в счёт.</summary>
public class TargetNearbyConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(TargetNearbyCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    private void Npc(uint wid, string name, float distance, NpcKind kind = NpcKind.Mob, NpcState state = NpcState.Standing)
        => _world.Npcs.Add(new NpcInfo(wid, wid, kind, state, 0, new Position(distance, 0, 0), name, 0));

    [Fact]
    public void ByName()
    {
        Npc(1, "Сидящий волк", 4);

        Assert.Equal("рядом «Сидящий волк» в 4,0 м", Check(new TargetNearbyCondition { Targets = ["сидящий волк"] }));
        Assert.Null(Check(new TargetNearbyCondition { Targets = ["Торговец"] }));
    }

    [Fact]
    public void NpcsAndOtherPetsCount()
    {
        Npc(1, "Торговец", 3, NpcKind.Npc);
        Npc(2, "Пет игрока", 5, NpcKind.Pet);

        Assert.NotNull(Check(new TargetNearbyCondition { Targets = ["Торговец"] }));
        Assert.NotNull(Check(new TargetNearbyCondition { Targets = ["Пет игрока"] }));
    }

    [Fact]
    public void OwnPetAndDeadDoNotCount()
    {
        _world.SetPet(1);
        Npc(1, "Сидящий волк", 3, state: NpcState.Dead);

        Assert.Null(Check(new TargetNearbyCondition()));
    }

    [Fact]
    public void CurrentTargetCounts()
    {
        Npc(1, "Сидящий волк", 3);
        _world.TargetWid = 1;

        Assert.NotNull(Check(new TargetNearbyCondition()));
    }

    [Fact]
    public void OnlyWithinRadius()
    {
        Npc(1, "Сидящий волк", 12);

        Assert.Null(Check(new TargetNearbyCondition { Radius = 10 }));
        Assert.NotNull(Check(new TargetNearbyCondition { Radius = 12 }));
    }

    [Fact]
    public void NearestIsNamed()
    {
        Npc(1, "Сидящий волк", 8);
        Npc(2, "Торговец", 3, NpcKind.Npc);

        Assert.Equal("рядом «Торговец» в 3,0 м", Check(new TargetNearbyCondition()));
    }

    [Fact]
    public void FileKeepsChoiceAndCleansIt()
    {
        var back = ClickerScript.RoundTrip(new TargetNearbyCondition { Targets = [" Торговец ", "торговец", "", "Сидящий волк"], Radius = -1 });

        Assert.Equal(["Торговец", "Сидящий волк"], back.Targets);
        Assert.Equal(1, back.Radius);
        Assert.Equal(GroundItemChoice.MaxRadius, ClickerScript.RoundTrip(new TargetNearbyCondition { Radius = 5000 }).Radius);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("рядом любая цель (до 10 м)", new TargetNearbyCondition().Describe());
        Assert.Equal("рядом цель: Сидящий волк, Торговец (до 5 м)", new TargetNearbyCondition { Targets = ["Сидящий волк", "Торговец"], Radius = 5 }.Describe());
    }

    [Fact]
    public void ChoiceEditsAreReported()
    {
        var condition = new TargetNearbyCondition();
        var edits = 0;
        condition.PropertyChanged += (_, e) => edits += e.PropertyName == nameof(ClickerCondition.Description) ? 0 : 1;

        condition.Targets.Add("Торговец");
        condition.Radius = 7;
        condition.Targets = ["Сидящий волк"];
        condition.Targets.Add("Торговец");

        Assert.Equal(4, edits);
        Assert.Equal(["Сидящий волк", "Торговец"], condition.Targets.ToArray());
    }
}

using System.Linq;
using BotCH.Core.Clicker;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «Рядом можно копать»: ресурсы и «нересурсы» по названиям, расстояние от персонажа; предметы не в счёт.</summary>
public class ResourceNearbyConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(ResourceNearbyCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    private void Resource(uint id, string name, float distance, bool special = false)
        => ClickerScript.Drop(_world, id, name, distance, GroundItemKind.Resource, special: special);

    [Fact]
    public void ByName()
    {
        Resource(1, "Медная руда", 4);

        Assert.Equal("рядом «Медная руда» в 4,0 м", Check(new ResourceNearbyCondition { Resources = ["медная руда"] }));
        Assert.Null(Check(new ResourceNearbyCondition { Resources = ["Лён"] }));
    }

    [Fact]
    public void QuestAndOtherSpecialCount()
    {
        // Квестовое, ящики — копаются, но не киркой: пользователь решает сам, копать ли их
        Resource(1, "Ящик с припасами", 3, special: true);

        Assert.NotNull(Check(new ResourceNearbyCondition { Resources = ["Ящик с припасами"] }));
        Assert.NotNull(Check(new ResourceNearbyCondition()));
    }

    [Fact]
    public void ItemsDoNotCount()
    {
        // Предмет подбирают, а не копают — даже с тем же названием
        ClickerScript.Drop(_world, 1, "Медная руда", 2);
        ClickerScript.Drop(_world, 2, "Монеты", 2, GroundItemKind.Money);

        Assert.Null(Check(new ResourceNearbyCondition { Resources = ["Медная руда"] }));
        Assert.Null(Check(new ResourceNearbyCondition()));
    }

    [Fact]
    public void OnlyWithinRadius()
    {
        Resource(1, "Медная руда", 12);

        Assert.Null(Check(new ResourceNearbyCondition { Radius = 10 }));
        Assert.NotNull(Check(new ResourceNearbyCondition { Radius = 12 }));
    }

    [Fact]
    public void NearestIsNamed()
    {
        Resource(1, "Медная руда", 8);
        Resource(2, "Лён", 3);

        Assert.Equal("рядом «Лён» в 3,0 м", Check(new ResourceNearbyCondition()));
    }

    [Fact]
    public void FileKeepsChoiceAndCleansIt()
    {
        var back = ClickerScript.RoundTrip(new ResourceNearbyCondition { Resources = [" Лён ", "лён", "", "Медная руда"], Radius = -1 });

        Assert.Equal(["Лён", "Медная руда"], back.Resources);
        Assert.Equal(1, back.Radius);
        Assert.Equal(GroundItemChoice.MaxRadius, ClickerScript.RoundTrip(new ResourceNearbyCondition { Radius = 5000 }).Radius);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("рядом любой ресурс (до 10 м)", new ResourceNearbyCondition().Describe());
        Assert.Equal("рядом ресурс: Лён, Медная руда (до 5 м)", new ResourceNearbyCondition { Resources = ["Лён", "Медная руда"], Radius = 5 }.Describe());
    }

    [Fact]
    public void ChoiceEditsAreReported()
    {
        var condition = new ResourceNearbyCondition();
        var edits = 0;
        condition.PropertyChanged += (_, e) => edits += e.PropertyName == nameof(ClickerCondition.Description) ? 0 : 1;

        condition.Resources.Add("Лён");
        condition.Radius = 7;
        condition.Resources = ["Медная руда"];
        condition.Resources.Add("Лён");

        Assert.Equal(4, edits);
        Assert.Equal(["Медная руда", "Лён"], condition.Resources.ToArray());
    }
}

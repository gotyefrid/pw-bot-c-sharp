using System.Linq;
using BotCH.Core.Clicker;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Условие «Рядом лежит предмет»: названия или группы, как у лута, и расстояние от персонажа.</summary>
public class ItemNearbyConditionTests
{
    private readonly FakeWorld _world = new();

    private string? Check(ItemNearbyCondition condition) => condition.Check(ClickerScript.Context(_world.Snapshot()));

    private void Drop(uint id, string name, float distance, GroundItemKind kind = GroundItemKind.Item, string? group = null)
        => ClickerScript.Drop(_world, id, name, distance, kind, group);

    [Fact]
    public void ByName()
    {
        Drop(1, "Мягкий мех", 4);

        Assert.Equal("лежит «Мягкий мех» в 4,0 м", Check(new ItemNearbyCondition { Items = ["мягкий мех"] }));
        Assert.Null(Check(new ItemNearbyCondition { Items = ["Сломанный меч"] }));
    }

    [Fact]
    public void ByGroup()
    {
        Drop(1, "Зелье здоровья", 6, group: "Банки");

        Assert.NotNull(Check(new ItemNearbyCondition { Items = ["Банки"] }));
    }

    [Fact]
    public void OnlyWithinRadius()
    {
        Drop(1, "Мягкий мех", 12);

        Assert.Null(Check(new ItemNearbyCondition { Items = ["Мягкий мех"], Radius = 10 }));
        Assert.NotNull(Check(new ItemNearbyCondition { Items = ["Мягкий мех"], Radius = 12 }));
    }

    [Fact]
    public void ResourcesDoNotCount()
    {
        // Ресурс копают, а не подбирают — даже с тем же названием
        Drop(1, "Мягкий мех", 2, GroundItemKind.Resource);

        Assert.Null(Check(new ItemNearbyCondition { Items = ["Мягкий мех"] }));
        Assert.Null(Check(new ItemNearbyCondition()));
    }

    [Fact]
    public void EmptyListMeansAnyItemOrMoney()
    {
        Drop(1, "Монеты", 3, GroundItemKind.Money);

        Assert.Equal("лежит «Монеты» в 3,0 м", Check(new ItemNearbyCondition()));
    }

    [Fact]
    public void NearestIsNamed()
    {
        Drop(1, "Мягкий мех", 8);
        Drop(2, "Мягкий мех", 3);

        Assert.Contains("3,0 м", Check(new ItemNearbyCondition { Items = ["Мягкий мех"] }));
    }

    [Fact]
    public void FileKeepsChoiceAndCleansIt()
    {
        var back = ClickerScript.RoundTrip(new ItemNearbyCondition { Items = [" Мех ", "мех", "", "Банки"], Radius = -1 });

        Assert.Equal(["Мех", "Банки"], back.Items);
        Assert.Equal(1, back.Radius);
        Assert.Equal(200, ClickerScript.RoundTrip(new ItemNearbyCondition { Radius = 5000 }).Radius);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("лежит любой предмет (до 10 м)", new ItemNearbyCondition().Describe());
        Assert.Equal("лежит: Мех, Банки (до 5 м)", new ItemNearbyCondition { Items = ["Мех", "Банки"], Radius = 5 }.Describe());
    }

    [Fact]
    public void ChoiceEditsAreReported()
    {
        // Выбор предметов в окне меняет список на месте — условие говорит о правке само
        var condition = new ItemNearbyCondition();
        var edits = 0;
        condition.PropertyChanged += (_, e) => edits += e.PropertyName == nameof(ClickerCondition.Description) ? 0 : 1;

        condition.Items.Add("Мех");
        condition.Items.Remove("Мех");
        condition.Radius = 7;
        condition.Items = ["Банки"];
        condition.Items.Add("Мех");

        Assert.Equal(5, edits);
        Assert.Equal(["Банки", "Мех"], condition.Items.ToArray());
    }
}

using System.Linq;
using BotCH.Core.Clicker;
using BotCH.Core.Logging;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>
/// Действие «Копать»: ближайшее из выбранного (пусто — что угодно копаемое, и «нересурсы») на любом расстоянии — как далеко,
/// задаёт условие; без проверки кирки, уровня, квеста. Без галочки «ждать» — отправило и дальше, с ней — ждёт итога.
/// </summary>
public class GatherResourceActionTests
{
    private readonly ClickerScript _script = new();

    private void Resource(uint id, string name, float distance, bool special = false)
        => ClickerScript.Drop(_script.World, id, name, distance, GroundItemKind.Resource, special: special);

    private string FirstTurn(GatherResourceAction action) => _script.Step(action).Run(_script.Start(), 1).Single();

    [Fact]
    public void NearestOfChosen()
    {
        Resource(1, "Медная руда", 8);
        Resource(2, "Медная руда", 3);
        Resource(3, "Лён", 1);

        Assert.Equal("gather 2", FirstTurn(new GatherResourceAction { Resources = ["медная руда"] }));
    }

    [Fact]
    public void NothingChosenIsNearestOfAnythingIncludingSpecial()
    {
        // Квестовое и ящики — тоже: пользователь решает сам
        Resource(1, "Ящик с припасами", 2, special: true);
        Resource(2, "Медная руда", 5);

        Assert.Equal("gather 1", FirstTurn(new GatherResourceAction()));
    }

    [Fact]
    public void AnyDistance()
    {
        Resource(1, "Медная руда", 150);

        Assert.Equal("gather 1", FirstTurn(new GatherResourceAction()));
    }

    [Fact]
    public void NotItems()
    {
        // Предмет подбирают, а не копают — даже с тем же названием
        ClickerScript.Drop(_script.World, 1, "Медная руда", 1);

        Assert.Equal("", FirstTurn(new GatherResourceAction { Resources = ["Медная руда"] }));
    }

    [Fact]
    public void NothingToDigIsNotWarning()
    {
        Assert.Equal(["key F1"], _script.Step(new GatherResourceAction()).Key("F1").Run(_script.Start(), 1));

        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Debug && e.Message.Contains("копать нечего"));
    }

    [Fact]
    public void DoesNotWaitByDefault()
    {
        Resource(1, "Медная руда", 8);

        Assert.Equal(["gather 1, key F1"], _script.Step(new GatherResourceAction()).Key("F1").Run(_script.Start(), 1));
    }

    [Fact]
    public void WaitsForResultWhenAsked()
    {
        Resource(1, "Медная руда", 8);
        var bot = _script.Step(new GatherResourceAction { WaitConfirmed = true }).Key("F1").Start();

        Assert.Equal(["gather 1", ""], _script.Run(bot, 2));

        // Накопал — ресурс пропал с земли
        _script.World.Ground.Clear();
        Assert.Equal(["key F1"], _script.Run(bot, 1));
    }

    [Fact]
    public void FileKeepsChoiceAndCleansIt()
        => Assert.Equal(["Лён", "Медная руда"],
            ClickerScript.RoundTrip(new GatherResourceAction { Resources = [" Лён ", "лён", "", "Медная руда"] }).Resources);

    [Fact]
    public void Described()
    {
        Assert.Equal("копать ближайший ресурс", new GatherResourceAction().Describe());
        Assert.Equal("копать: Лён, Медная руда", new GatherResourceAction { Resources = ["Лён", "Медная руда"] }.Describe());
    }

    [Fact]
    public void ChoiceEditsAreReported()
    {
        var action = new GatherResourceAction();
        var edits = 0;
        action.PropertyChanged += (_, _) => edits++;

        action.Resources.Add("Лён");
        action.Resources.Remove("Лён");
        action.Resources = ["Медная руда"];
        action.Resources.Add("Лён");

        Assert.Equal(4, edits);
        Assert.Equal(["Медная руда", "Лён"], action.Resources.ToArray());
    }
}

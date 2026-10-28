using System.Linq;
using Notes.Core.Clicker;
using Notes.Core.Logging;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Действие «Поднять предмет»: ближайший из выбранных (пусто — любой) на любом расстоянии — как далеко, задаёт условие;
/// без галочки «ждать» — отправило подбор и дальше, с ней — ждёт, пока предмет пропадёт с земли; поднимать нечего — дальше
/// без предупреждения. Сама галочка (файл, подпись) — в <see cref="WaitConfirmedTests"/>.
/// </summary>
public class PickUpItemActionTests
{
    private readonly ClickerScript _script = new();

    private void Drop(uint id, string name, float distance, GroundItemKind kind = GroundItemKind.Item, string? group = null)
        => ClickerScript.Drop(_script.World, id, name, distance, kind, group);

    private string FirstTurn(PickUpItemAction action) => _script.Step(action).Run(_script.Start(), 1).Single();

    [Fact]
    public void NearestOfChosen()
    {
        Drop(1, "Мягкий мех", 8);
        Drop(2, "Мягкий мех", 3);
        Drop(3, "Сломанный меч", 1);

        Assert.Equal("pickup-approach 2", FirstTurn(new PickUpItemAction { Items = ["мягкий мех"] }));
    }

    [Fact]
    public void ChosenByGroup()
    {
        Drop(1, "Мягкий мех", 2);
        Drop(2, "Зелье здоровья", 6, group: "Банки");

        Assert.Equal("pickup-approach 2", FirstTurn(new PickUpItemAction { Items = ["Банки"] }));
    }

    [Fact]
    public void NothingChosenIsNearestAnyWithMoney()
    {
        Drop(1, "Мягкий мех", 5);
        Drop(2, "Монеты", 2, GroundItemKind.Money);

        Assert.Equal("pickup-approach 2", FirstTurn(new PickUpItemAction()));
    }

    [Fact]
    public void AnyDistance()
    {
        Drop(1, "Мягкий мех", 150);

        Assert.Equal("pickup-approach 1", FirstTurn(new PickUpItemAction()));
    }

    [Fact]
    public void NotResources()
    {
        // Ресурс копают, а не подбирают
        Drop(1, "Мягкий мех", 1, GroundItemKind.Resource);

        Assert.Equal("", FirstTurn(new PickUpItemAction()));
    }

    [Fact]
    public void NothingToPickIsNotWarning()
    {
        var turns = _script.Step(new PickUpItemAction()).Key("F1").Run(_script.Start(), 1);

        Assert.Equal(["key F1"], turns);
        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Debug && e.Message.Contains("рядом нечего поднять"));
    }

    [Fact]
    public void DoesNotWaitForPickup()
    {
        // Подбор ушёл — сразу следующий шаг: подбежит и поднимет игра сама
        Drop(1, "Мягкий мех", 8);

        Assert.Equal(["pickup-approach 1, key F1"], _script.Step(new PickUpItemAction()).Key("F1").Run(_script.Start(), 1));
    }

    [Fact]
    public void SameItemIsNotSentAgainWhileGameIsOnIt()
    {
        // По кругу — тот же предмет, а персонаж к нему ещё бежит: второй раз в игру не шлётся
        Drop(1, "Мягкий мех", 8);

        Assert.Equal(["pickup-approach 1", "", ""], _script.Step(new PickUpItemAction()).Run(_script.Start(), 3));
    }

    [Fact]
    public void WaitsUntilItemIsGoneWhenAsked()
    {
        Drop(1, "Мягкий мех", 8);
        var bot = _script.Step(new PickUpItemAction { WaitConfirmed = true }).Key("F1").Start();

        Assert.Equal(["pickup-approach 1", ""], _script.Run(bot, 2));

        _script.World.Ground.Clear();
        Assert.Equal(["key F1"], _script.Run(bot, 1));
    }

    [Fact]
    public void NotGivenWhileWaitingIsFailure()
    {
        // Стоим рядом, а предмет не пропадает (чужой лут) — «не вышло» в лог, и дальше по сценарию; по кругу — снова к нему:
        // памяти между запусками нет — как быть с таким предметом, решает сценарий
        Drop(1, "Мягкий мех", 1);
        var turns = _script.Step(new PickUpItemAction { WaitConfirmed = true }).Key("F1").Run(_script.Start(), 20);

        Assert.Equal("pickup-approach 1", turns[0]);
        Assert.Contains("key F1, pickup-approach 1", turns);
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("не вышло"));
    }

    [Fact]
    public void FileKeepsChoiceAndCleansIt()
    {
        var back = ClickerScript.RoundTrip(new PickUpItemAction { Items = [" Мех ", "мех", "", "Банки"] });

        Assert.Equal(["Мех", "Банки"], back.Items);
    }

    [Fact]
    public void Described()
    {
        Assert.Equal("поднять ближайший предмет", new PickUpItemAction().Describe());
        Assert.Equal("поднять: Мех, Банки", new PickUpItemAction { Items = ["Мех", "Банки"] }.Describe());
    }

    [Fact]
    public void ChoiceEditsAreReported()
    {
        // Выбор предметов в окне меняет список на месте — действие говорит о правке само: шаг сохранится
        var action = new PickUpItemAction();
        var edits = 0;
        action.PropertyChanged += (_, _) => edits++;

        action.Items.Add("Мех");
        action.Items.Remove("Мех");
        action.Items = ["Банки"];
        action.Items.Add("Мех");

        Assert.Equal(4, edits);
        Assert.Equal(["Банки", "Мех"], action.Items.ToArray());
    }
}

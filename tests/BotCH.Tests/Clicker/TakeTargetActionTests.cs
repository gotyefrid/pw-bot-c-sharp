using System.Linq;
using BotCH.Core.Clicker;
using BotCH.Core.Logging;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>
/// Действие «Взять цель»: ближайший из выбранных (пусто — кто угодно: моб, NPC, чужой пет; свой пет — нет) на любом
/// расстоянии — как далеко, задаёт условие. Уже в цели — ничего не шлёт. Без галочки «ждать» — отправило и дальше, с ней —
/// ждёт, пока в цели окажется тот, кого брали.
/// </summary>
public class TakeTargetActionTests
{
    private readonly ClickerScript _script = new();

    private void Npc(uint wid, string name, float distance, NpcKind kind = NpcKind.Mob, NpcState state = NpcState.Standing)
        => _script.World.Npcs.Add(new NpcInfo(wid, wid, kind, state, 0, new Position(distance, 0, 0), name, 0));

    private string FirstTurn(TakeTargetAction action) => _script.Step(action).Run(_script.Start(), 1).Single();

    [Fact]
    public void NearestOfChosen()
    {
        Npc(1, "Сидящий волк", 8);
        Npc(2, "Сидящий волк", 3);
        Npc(3, "Колючий дикобраз", 1);

        Assert.Equal("select 2", FirstTurn(new TakeTargetAction { Targets = ["сидящий волк"] }));
    }

    [Fact]
    public void NothingChosenIsNearestOfAnyone()
    {
        Npc(1, "Сидящий волк", 5);
        Npc(2, "Торговец", 2, NpcKind.Npc);

        Assert.Equal("select 2", FirstTurn(new TakeTargetAction()));
    }

    [Fact]
    public void OtherPetsCanBeTaken()
    {
        Npc(1, "Сидящий волк", 5);
        Npc(2, "Пет игрока", 3, NpcKind.Pet);

        Assert.Equal("select 2", FirstTurn(new TakeTargetAction { Targets = ["Пет игрока"] }));
    }

    [Fact]
    public void NeverOwnPet()
    {
        // Свой пет всегда рядом — «ближайшей целью» всегда был бы он
        _script.World.SetPet(1);
        Npc(5, "Сидящий волк", 6);

        Assert.Equal("select 5", FirstTurn(new TakeTargetAction()));
    }

    [Fact]
    public void NotDead()
    {
        Npc(1, "Сидящий волк", 1, state: NpcState.Dead);
        Npc(2, "Сидящий волк", 5);

        Assert.Equal("select 2", FirstTurn(new TakeTargetAction()));
    }

    [Fact]
    public void AnyDistance()
    {
        Npc(1, "Сидящий волк", 150);

        Assert.Equal("select 1", FirstTurn(new TakeTargetAction()));
    }

    [Fact]
    public void AlreadyTakenSendsNothing()
    {
        Npc(1, "Сидящий волк", 3);
        _script.World.TargetWid = 1;

        Assert.Equal(["key F1"], _script.Step(new TakeTargetAction()).Key("F1").Run(_script.Start(), 1));
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Debug && e.Message.Contains("«Сидящий волк» уже в цели"));
    }

    [Fact]
    public void SwitchesToNearerChosen()
    {
        // Не сбивать взятую цель — условием «цели нет»
        Npc(1, "Сидящий волк", 8);
        Npc(2, "Сидящий волк", 3);
        _script.World.TargetWid = 1;

        Assert.Equal("select 2", FirstTurn(new TakeTargetAction()));
    }

    [Fact]
    public void NobodyIsNotWarning()
    {
        Assert.Equal(["key F1"], _script.Step(new TakeTargetAction()).Key("F1").Run(_script.Start(), 1));

        Assert.DoesNotContain(_script.Log, e => e.Level >= LogLevel.Warning);
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Debug && e.Message.Contains("взять в цель некого"));
    }

    [Fact]
    public void DoesNotWaitByDefault()
    {
        Npc(1, "Сидящий волк", 8);

        Assert.Equal(["select 1, key F1"], _script.Step(new TakeTargetAction()).Key("F1").Run(_script.Start(), 1));
    }

    [Fact]
    public void WaitsForThatTargetWhenAsked()
    {
        Npc(1, "Сидящий волк", 8);
        Npc(2, "Колючий дикобраз", 9);
        var bot = _script.Step(new TakeTargetAction { Targets = ["Сидящий волк"], WaitConfirmed = true }).Key("F1").Start();

        Assert.Equal(["select 1", ""], _script.Run(bot, 2));

        // В цели оказался другой — это не подтверждение
        _script.World.TargetWid = 2;
        Assert.Equal([""], _script.Run(bot, 1));

        _script.World.TargetWid = 1;
        Assert.Equal(["key F1"], _script.Run(bot, 1));
    }

    [Fact]
    public void NotTakenIsReportedAndScriptGoesOn()
    {
        Npc(1, "Сидящий волк", 8);
        var turns = _script.Step(new TakeTargetAction { WaitConfirmed = true }).Key("F1").Run(_script.Start(), 9);

        // Не взялась за 2 с — «не вышло», следующий шаг и новый круг (снова взять) — в тот же ход
        Assert.Equal(["select 1", "", "", "", "", "", "", "", "key F1, select 1"], turns);
        Assert.Contains(_script.Log, e => e.Level == LogLevel.Warning && e.Message.Contains("не вышло"));
    }

    [Fact]
    public void FileKeepsChoiceAndCleansIt()
        => Assert.Equal(["Сидящий волк", "Торговец"],
            ClickerScript.RoundTrip(new TakeTargetAction { Targets = [" Сидящий волк ", "сидящий волк", "", "Торговец"] }).Targets);

    [Fact]
    public void Described()
    {
        Assert.Equal("взять ближайшую цель", new TakeTargetAction().Describe());
        Assert.Equal("взять в цель: Сидящий волк, Торговец", new TakeTargetAction { Targets = ["Сидящий волк", "Торговец"] }.Describe());
    }

    [Fact]
    public void ChoiceEditsAreReported()
    {
        var action = new TakeTargetAction();
        var edits = 0;
        action.PropertyChanged += (_, _) => edits++;

        action.Targets.Add("Торговец");
        action.Targets.Remove("Торговец");
        action.Targets = ["Сидящий волк"];
        action.Targets.Add("Торговец");

        Assert.Equal(4, edits);
        Assert.Equal(["Сидящий волк", "Торговец"], action.Targets.ToArray());
    }
}

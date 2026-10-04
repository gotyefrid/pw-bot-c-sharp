using System;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Brain;

/// <summary>Мозг без режимов: любой набор поведений — так же встанет кликер ([банки?, пет?, шаги]).</summary>
public class BotBrainTests
{
    private readonly FakeWorld _world = new();

    private sealed class Step(string name, bool acts, string? status = null) : IBehavior
    {
        public int Ticks { get; private set; }
        public string Name => name;
        public string? Status => status;

        public bool Tick(BrainContext c)
        {
            Ticks++;
            return acts;
        }

        public void OnOutcome(BrainContext c, ActionOutcome outcome)
        {
        }

        public void Reset()
        {
        }
    }

    private static BotBrain Brain(IBehavior main, params IBehavior[] behaviors)
        => new(new BrainContext(new ActionRunner(new GameControl(new FakeActions()), NullLogger.Instance), new ClassSkills(),
            NullLogger.Instance, new Random(1)), behaviors, main);

    [Fact]
    public void FirstBehaviorThatActsEndsTheTurn()
    {
        var idle = new Step("ждёт", false);
        var busy = new Step("занят", true, "делаю шаг");
        var last = new Step("последний", true);
        var brain = Brain(last, idle, busy, last);

        brain.Tick(_world.Snapshot());

        Assert.Equal((1, 1, 0), (idle.Ticks, busy.Ticks, last.Ticks));
        Assert.Equal("делаю шаг", brain.Status);
    }

    [Fact]
    public void MainStatusIsShownWhenNobodyActs()
    {
        // Пет «жду пета» хода не занял, бой «ищу цель» — тоже: в окне то, что говорит главное поведение
        var pet = new Step("пет", false, "жду пета");
        var combat = new Step("бой", false, "ищу цель");
        var brain = Brain(combat, pet, combat);

        brain.Tick(_world.Snapshot());

        Assert.Equal("ищу цель", brain.Status);
    }

    [Fact]
    public void MainMustBeInTheSet()
        => Assert.Throws<ArgumentException>(() => Brain(new Step("чужой", false), new Step("свой", false)));
}

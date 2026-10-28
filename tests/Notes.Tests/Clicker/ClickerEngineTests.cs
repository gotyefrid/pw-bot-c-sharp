using Notes.Core.Clicker;
using Notes.Core.Logging;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Механизм сценария — порядок, круг, пропуск, остановка, текущий шаг. Виды действий и условий здесь только простые
/// (клавиша, пауза, «всегда», «HP меньше %» как ложное условие); каждый вид проверяется в своём файле.
/// </summary>
public class ClickerEngineTests
{
    private readonly ClickerScript _script = new();

    // HP в подставном мире 80 %: «меньше 50 %» — ложно, пока тест не опустит HP
    private static ClickerCondition LowHp => new HpBelowCondition { Percent = 50 };

    [Fact]
    public void StepsGoInOrderAndCircleEveryTurn()
    {
        // Без пауз круг — за снимок: каждый шаг по разу, ни один дважды
        _script.Key("1").Key("2").Key("1");

        Assert.Equal(["key 1, key 2, key 1", "key 1, key 2, key 1"], _script.Run(_script.Start(), 2));
    }

    [Fact]
    public void NextStepWaitsForRunningOne()
    {
        _script.Key("F1").Pause(1000).Key("F2");
        var bot = _script.Start();

        // 0 с: F1, пауза пошла; 1 с: пауза кончилась — F2, снова F1 и новая пауза в тот же снимок
        Assert.Equal(["key F1", "", "", "", "key F2, key F1", ""], _script.Run(bot, 6));
        Assert.Contains("пауза 1000 мс", bot.Status);
    }

    [Fact]
    public void StepWithFalseConditionIsSkipped()
    {
        _script.Key("F1", LowHp).Key("2");
        var bot = _script.Start();

        Assert.Equal(["key 2", "key 2"], _script.Run(bot, 2));

        _script.World.Hp = 200;
        Assert.Equal(["key F1, key 2"], _script.Run(bot, 1));
    }

    [Fact]
    public void ConditionOfLongStepCountsToo()
    {
        // «Пауза 5000 мс, если HP меньше 50 %» — пока HP полное, пауза не держит
        _script.Key("F1").Pause(5000, LowHp);

        Assert.Equal(["key F1", "key F1"], _script.Run(_script.Start(), 2));
    }

    [Fact]
    public void NothingToDoWaitsForNextSnapshot()
    {
        _script.Key("F1", LowHp);
        var bot = _script.Start();

        Assert.Equal(["", ""], _script.Run(bot, 2));
        Assert.Contains("жду", bot.Status);
    }

    [Fact]
    public void UnreadyStepIsSkipped()
    {
        _script.Key("").Key("F2");

        Assert.Equal(["key F2"], _script.Run(_script.Start(), 1));
        Assert.Null(_script.Stopped);
    }

    [Fact]
    public void NothingReadyStopsTheBot()
    {
        _script.Key("").Key("Alt+F4");

        _script.Run(_script.Start(), 1);

        Assert.Empty(_script.Game.Calls);
        Assert.Equal("в кликере нет ни одного готового действия", _script.Stopped);
    }

    [Fact]
    public void FailedStepIsLoggedOnceAndScriptGoesOn()
    {
        _script.Game.Refuse.Add("key F1");
        _script.Key("F1").Key("F2");

        Assert.Equal(["key F1, key F2", "key F1, key F2"], _script.Run(_script.Start(), 2));
        Assert.Single(_script.Log, e => e.Level == LogLevel.Warning && e.Message.StartsWith("Кликер: шаг 1"));
    }

    [Fact]
    public void WindowSeesCurrentStep()
    {
        _script.Key("F1").Pause(1000);
        var bot = _script.Start();
        var progress = bot.Part<IClickerProgress>()!;

        Assert.Null(progress.Current);
        _script.Run(bot, 1);
        Assert.Equal(1, progress.Current);

        bot.Reset();
        Assert.Null(progress.Current);
    }

    [Fact]
    public void StepsChangedOnTheGo()
    {
        // Шаги убрали, пока шла пауза третьего: доделать её и начать с первого
        _script.Key("F1").Key("F2").Pause(500);
        var bot = _script.Start();
        _script.Run(bot, 1);

        _script.Settings.Clicker.Steps.RemoveAt(1);
        bot.UpdateSettings(_script.Settings);

        Assert.Equal(["", "key F1", ""], _script.Run(bot, 3));
    }

    [Fact]
    public void DoesNothingElse()
    {
        // Ни банок, ни боя, ни пета: только сценарий
        _script.World.Hp = 50;
        _script.World.AddPotion(1, 8617, 5, hp: 300);
        _script.World.AddMob(1, "Волк", 5);
        _script.Key("F3", new HpBelowCondition { Percent = 1 });

        _script.Run(_script.Start(), 8);

        Assert.Empty(_script.Game.Calls);
    }
}

using System;
using System.Linq;
using BotCH.Core.Clicker;
using BotCH.Core.Settings;
using Xunit;

namespace BotCH.Tests.Settings;

/// <summary>Шаги кликера в файле персонажа: виды действий и условий, неизвестные виды, пределы значений.</summary>
public class ClickerSettingsTests
{
    [Fact]
    public void StepsSurviveSaveAndLoad()
    {
        var s = new BotSettings();
        s.Clicker.Steps.Add(new ClickerStep { Action = new PressKeyAction { Key = "F1" }, Condition = new HpBelowCondition { Percent = 40 } });
        s.Clicker.Steps.Add(new ClickerStep { Action = new PauseAction { Milliseconds = 2500 } });
        s.Clicker.Steps.Add(new ClickerStep
        {
            Action = new PressKeyAction { Key = "Ctrl+2" },
            Condition = new ItemNearbyCondition { Items = ["Мягкий мех", "Банки"], Radius = 8 },
        });

        var json = SettingsJson.Serialize(s);
        var steps = SettingsJson.Parse(json).Clicker.Steps;

        Assert.Contains("\"kind\": \"PressKey\"", json);
        Assert.Contains("\"kind\": \"HpBelow\"", json);
        Assert.Equal(3, steps.Count);
        Assert.Equal(("F1", 40), (Assert.IsType<PressKeyAction>(steps[0].Action).Key, Assert.IsType<HpBelowCondition>(steps[0].Condition).Percent));
        Assert.Equal(2500, Assert.IsType<PauseAction>(steps[1].Action).Milliseconds);
        Assert.IsType<AlwaysCondition>(steps[1].Condition);
        var items = Assert.IsType<ItemNearbyCondition>(steps[2].Condition);
        Assert.Equal(8, items.Radius);
        Assert.Equal(["Мягкий мех", "Банки"], items.Items);
    }

    [Fact]
    public void KindIsWrittenFirst()
    {
        var s = new BotSettings();
        s.Clicker.Steps.Add(new ClickerStep { Action = new PauseAction { Milliseconds = 3000 } });

        var json = SettingsJson.Serialize(s);

        Assert.True(json.IndexOf("\"kind\": \"Pause\"", StringComparison.Ordinal) < json.IndexOf("\"milliseconds\"", StringComparison.Ordinal));
    }

    [Fact]
    public void UnknownKindsAreKeptAsTheyWere()
    {
        // Файл от более новой версии бота: шаг не выполняется, но и не теряется
        var s = SettingsJson.Parse("""
            { "clicker": { "steps": [
                { "action": { "kind": "Teleport", "town": "Город Мечей", "price": 5 }, "condition": { "kind": "MoonIsFull", "phase": 1 } }
            ] } }
            """);

        var step = Assert.Single(s.Clicker.Steps);
        Assert.Equal("Teleport", Assert.IsType<UnknownAction>(step.Action).Kind);
        Assert.NotNull(step.Action.Problem);
        Assert.Equal("MoonIsFull", Assert.IsType<UnknownCondition>(step.Condition).Kind);

        var again = SettingsJson.Parse(SettingsJson.Serialize(s)).Clicker.Steps[0];
        var action = Assert.IsType<UnknownAction>(again.Action);
        Assert.Equal(("Город Мечей", 5), ((string)action.Fields["town"]!, (int)action.Fields["price"]!));
        Assert.Equal(1, (int)Assert.IsType<UnknownCondition>(again.Condition).Fields["phase"]!);
    }

    [Fact]
    public void BadValuesAreCleanedButStepsStay()
    {
        // Шаг без клавиши — только что добавленный, ещё не настроен: не выкидывается
        var s = SettingsJson.Parse("""
            { "clicker": { "steps": [
                { "action": { "kind": "PressKey", "key": "" } },
                { "action": { "kind": "PressKey", "key": "Alt+F4" } },
                { "action": { "kind": "pressKey", "key": "ctrl+f2" }, "condition": { "kind": "HpBelow", "percent": 500 } },
                { "action": { "kind": "Pause", "milliseconds": -3 }, "condition": { "kind": "ItemNearby", "radius": -1, "items": [" Мех ", "мех", ""] } },
                { },
                null
            ] } }
            """);

        var steps = s.Clicker.Steps;
        Assert.Equal(5, steps.Count);
        Assert.Equal(["", "", "Ctrl+F2"], steps.Take(3).Select(st => ((PressKeyAction)st.Action).Key));
        Assert.NotNull(steps[1].Action.Problem);
        Assert.Equal(100, ((HpBelowCondition)steps[2].Condition).Percent);
        Assert.Equal(0, ((PauseAction)steps[3].Action).Milliseconds);
        var items = (ItemNearbyCondition)steps[3].Condition;
        Assert.Equal(1, items.Radius);
        Assert.Equal(["Мех"], items.Items);
        Assert.IsType<PressKeyAction>(steps[4].Action);
        Assert.IsType<AlwaysCondition>(steps[4].Condition);
    }

    [Fact]
    public void EveryKindIsListedOnceAndWorksInFile()
    {
        // Новый класс действия или условия без строки в ClickerKinds — файл его не прочтёт: ловим здесь
        foreach (var (kinds, baseType, unknown) in new[]
                 {
                     (ClickerKinds.Actions.All, typeof(ClickerAction), typeof(UnknownAction)),
                     (ClickerKinds.Conditions.All, typeof(ClickerCondition), typeof(UnknownCondition)),
                 })
        {
            var classes = baseType.Assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && t != unknown);
            Assert.Equal(classes.OrderBy(t => t.Name), kinds.Select(k => k.Class).OrderBy(t => t.Name));
            Assert.Equal(kinds.Count, kinds.Select(k => k.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        var s = new BotSettings();
        foreach (var action in ClickerKinds.Actions.All)
            s.Clicker.Steps.Add(new ClickerStep { Action = ClickerKinds.Actions.Create(action.Id)! });
        foreach (var condition in ClickerKinds.Conditions.All)
            s.Clicker.Steps.Add(new ClickerStep { Condition = ClickerKinds.Conditions.Create(condition.Id)! });

        var back = SettingsJson.Parse(SettingsJson.Serialize(s)).Clicker.Steps;

        Assert.Equal(s.Clicker.Steps.Select(st => (st.Action.GetType(), st.Condition.GetType())),
            back.Select(st => (st.Action.GetType(), st.Condition.GetType())));
    }

    [Fact]
    public void StepsAreDescribedForPlayer()
    {
        Assert.Equal("клавиша не выбрана", new PressKeyAction().Describe());
        Assert.Equal("клавиша F1", new PressKeyAction { Key = "F1" }.Describe());
        Assert.Equal("пауза 500 мс", new PauseAction { Milliseconds = 500 }.Describe());
        Assert.Equal("всегда", new AlwaysCondition().Describe());
        Assert.Equal("HP меньше 30 %", new HpBelowCondition { Percent = 30 }.Describe());
        Assert.Equal("лежит любой предмет (до 10 м)", new ItemNearbyCondition().Describe());
        Assert.Equal("лежит: Мех, Банки (до 5 м)", new ItemNearbyCondition { Items = ["Мех", "Банки"], Radius = 5 }.Describe());
    }

    [Fact]
    public void FieldsReportTheirEdits()
    {
        // Окно привязано к полям действия и условия напрямую — о правке они говорят сами, и выбор предметов тоже
        var condition = new ItemNearbyCondition();
        var key = new PressKeyAction();
        var edits = 0;
        condition.PropertyChanged += (_, _) => edits++;
        key.PropertyChanged += (_, _) => edits++;

        condition.Items.Add("Мех");
        condition.Radius = 7;
        key.Key = "F3";
        key.Key = "F3";

        Assert.Equal(3, edits);
    }

    [Fact]
    public void OldFileWithoutClickerHasNoSteps()
        => Assert.Empty(SettingsJson.Parse("""{ "mode": "Clicker" }""").Clicker.Steps);
}

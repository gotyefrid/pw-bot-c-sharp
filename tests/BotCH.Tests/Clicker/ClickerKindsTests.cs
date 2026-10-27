using System;
using System.Linq;
using BotCH.Core.Clicker;
using BotCH.Core.Settings;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>
/// Каталог видов и файл настроек в целом: каждый вид записан в каталоге и переживает файл, неизвестный вид не теряется,
/// пустые шаги чинятся. Сами виды — каждый в своём файле тестов.
/// </summary>
public class ClickerKindsTests
{
    [Fact]
    public void EveryClassIsInCatalogOnce()
    {
        // Новый класс действия или условия без строки в ClickerKinds файл не прочтёт — ловим здесь
        foreach (var (kinds, baseType, unknown) in new[]
                 {
                     (ClickerKinds.Actions.All, typeof(ClickerAction), typeof(UnknownAction)),
                     (ClickerKinds.Conditions.All, typeof(ClickerCondition), typeof(UnknownCondition)),
                 })
        {
            var classes = baseType.Assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t) && !t.IsAbstract && t != unknown);
            Assert.Equal(classes.OrderBy(t => t.Name), kinds.Select(k => k.Class).OrderBy(t => t.Name));
            Assert.Equal(kinds.Count, kinds.Select(k => k.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(kinds, k => Assert.False(string.IsNullOrWhiteSpace(k.Title)));
        }
    }

    [Fact]
    public void EveryKindSurvivesFile()
    {
        foreach (var kind in ClickerKinds.Actions.All)
            Assert.IsType(kind.Class, ClickerScript.RoundTrip(ClickerKinds.Actions.Create(kind.Id)!));
        foreach (var kind in ClickerKinds.Conditions.All)
            Assert.IsType(kind.Class, ClickerScript.RoundTrip(ClickerKinds.Conditions.Create(kind.Id)!));
    }

    [Fact]
    public void KindIsWrittenFirstAndCaseDoesNotMatterOnRead()
    {
        var json = ClickerScript.Json(new PauseAction { Milliseconds = 3000 });
        Assert.True(json.IndexOf("\"kind\": \"Pause\"", StringComparison.Ordinal) < json.IndexOf("\"milliseconds\"", StringComparison.Ordinal));

        var step = SettingsJson.Parse("""{ "clicker": { "steps": [ { "action": { "kind": "pause", "milliseconds": 7 } } ] } }""").Clicker.Steps[0];
        Assert.Equal(7, Assert.IsType<PauseAction>(step.Action).Milliseconds);
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
        var condition = Assert.IsType<UnknownCondition>(Assert.Single(step.Conditions));
        Assert.Equal("MoonIsFull", condition.Kind);
        Assert.Null(condition.Check(ClickerScript.Context(new Fakes.FakeWorld().Snapshot())));

        var again = SettingsJson.Parse(SettingsJson.Serialize(s)).Clicker.Steps[0];
        var action = Assert.IsType<UnknownAction>(again.Action);
        Assert.Equal("Город Мечей", (string)action.Fields["town"]!);
        Assert.Equal(5, (int)action.Fields["price"]!);
        Assert.Equal(1, (int)Assert.IsType<UnknownCondition>(Assert.Single(again.Conditions)).Fields["phase"]!);
    }

    [Fact]
    public void EmptyStepsAreRepairedNullOnesDropped()
    {
        var steps = SettingsJson.Parse("""
            { "clicker": { "steps": [ { }, null, { "action": null, "conditions": null }, { "conditions": [ null, { "kind": "Always" } ] } ] } }
            """).Clicker.Steps;

        Assert.Equal(3, steps.Count);
        Assert.All(steps, st => Assert.IsType<PressKeyAction>(st.Action));
        Assert.Empty(steps[0].Conditions);
        Assert.Empty(steps[1].Conditions);
        Assert.IsType<AlwaysCondition>(Assert.Single(steps[2].Conditions));
    }

    [Fact]
    public void StepOrderSurvivesFile()
    {
        var s = new BotSettings();
        foreach (var key in new[] { "1", "2", "3" })
            s.Clicker.Steps.Add(new ClickerStep { Action = new PressKeyAction { Key = key } });

        var keys = SettingsJson.Parse(SettingsJson.Serialize(s)).Clicker.Steps.Select(st => ((PressKeyAction)st.Action).Key);

        Assert.Equal(["1", "2", "3"], keys);
    }

    [Fact]
    public void OldFileWithoutClickerHasNoSteps()
        => Assert.Empty(SettingsJson.Parse("""{ "mode": "Clicker" }""").Clicker.Steps);
}

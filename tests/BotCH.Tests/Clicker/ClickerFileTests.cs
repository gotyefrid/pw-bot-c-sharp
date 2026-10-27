using System;
using System.IO;
using System.Linq;
using BotCH.Core.Clicker;
using Xunit;

namespace BotCH.Tests.Clicker;

/// <summary>Сценарий в своём файле — экспорт и импорт: туда и обратно, чужие и испорченные файлы, шаги новых версий.</summary>
public sealed class ClickerFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "clicker-file-tests-" + Guid.NewGuid().ToString("N"));

    public ClickerFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string FilePath(string name = "Кликер.json") => Path.Combine(_folder, name);

    private string WriteFile(string text)
    {
        var path = FilePath();
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void SavedScriptLoadsBackTheSame()
    {
        ClickerStep[] steps =
        [
            new() { Action = new PressKeyAction { Key = "Tab" }, Conditions = { new TargetCondition { Is = TargetIs.None } } },
            new() { Action = new PickUpItemAction { Items = ["Мех"], WaitConfirmed = true } },
            new()
            {
                Action = new PauseAction { Milliseconds = 500 },
                Conditions = { new AnyCondition { Not = true, Conditions = { new HpBelowCondition { Percent = 30 }, new MpBelowCondition() } } },
            },
        ];

        ClickerFile.Save(FilePath(), steps);
        var back = ClickerFile.Load(FilePath(), out var problem);

        Assert.Null(problem);
        Assert.NotNull(back);
        Assert.Equal(steps.Select(s => s.Action.Describe()), back.Select(s => s.Action.Describe()));
        Assert.Equal(steps.Select(s => s.DescribeConditions()), back.Select(s => s.DescribeConditions()));
    }

    [Fact]
    public void FileIsReadableText()
    {
        var text = ClickerFile.Serialize([new ClickerStep { Action = new PressKeyAction { Key = "F1" } }]);

        Assert.Contains("\"steps\"", text);
        Assert.Contains("\"kind\": \"PressKey\"", text);
        Assert.Contains("\"key\": \"F1\"", text);
    }

    [Fact]
    public void StepsOfNewerBotAreKept()
    {
        var steps = ClickerFile.Parse("""{ "steps": [ { "action": { "kind": "Teleport", "town": "Город Мечей" } } ] }""", out _);

        var again = ClickerFile.Parse(ClickerFile.Serialize(steps!), out _);

        Assert.Equal("Город Мечей", (string)Assert.IsType<UnknownAction>(Assert.Single(again!).Action).Fields["town"]!);
    }

    [Fact]
    public void ValuesAreBroughtIntoLimits()
    {
        var steps = ClickerFile.Parse("""{ "steps": [ null, { "action": { "kind": "Pause", "milliseconds": -5 } } ] }""", out _);

        Assert.Equal(0, Assert.IsType<PauseAction>(Assert.Single(steps!).Action).Milliseconds);
    }

    [Fact]
    public void EmptyScriptIsScript() => Assert.Empty(ClickerFile.Parse("""{ "steps": [] }""", out _)!);

    [Theory]
    [InlineData("""{ "mode": "Clicker" }""")]
    [InlineData("""[ 1, 2 ]""")]
    [InlineData("""{ "steps": 5 }""")]
    public void OtherFileIsNotScript(string text)
    {
        Assert.Null(ClickerFile.Parse(text, out var problem));
        Assert.Equal("в файле нет сценария кликера", problem);
    }

    [Theory]
    [InlineData("""{ "steps": [ """)]
    [InlineData("не json")]
    [InlineData("""{ "steps": [ 5 ] }""")]
    public void BrokenFileIsReported(string text)
    {
        Assert.Null(ClickerFile.Parse(text, out var problem));
        Assert.StartsWith("файл испорчен", problem);
    }

    [Fact]
    public void MissingFileIsReported()
    {
        Assert.Null(ClickerFile.Load(FilePath("нет такого.json"), out var problem));
        Assert.StartsWith("не удалось прочитать файл", problem);
    }

    [Fact]
    public void FileOpenInOtherProgramIsRead()
    {
        var path = WriteFile("""{ "steps": [ { "action": { "kind": "PressKey", "key": "F2" } } ] }""");
        using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("клавиша F2", Assert.Single(ClickerFile.Load(path, out _)!).Action.Describe());
    }

    [Fact]
    public void SaveReplacesOldFile()
    {
        var path = WriteFile("старое");

        ClickerFile.Save(path, [new ClickerStep { Action = new PressKeyAction { Key = "F3" } }]);

        Assert.Equal("клавиша F3", Assert.Single(ClickerFile.Load(path, out _)!).Action.Describe());
    }
}

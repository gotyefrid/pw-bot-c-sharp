using System;
using System.IO;
using Notes.Core.Settings;
using Xunit;

namespace Notes.Tests.Settings;

/// <summary>tabs.json: какие вкладки персонажей открыть при запуске.</summary>
public class OpenTabsFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "notes-tabs-" + Guid.NewGuid().ToString("N"));

    public OpenTabsFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void NoFileMeansNoTabs()
    {
        var (tabs, selected) = new OpenTabsFile(_folder).Load(out var problem);

        Assert.Empty(tabs);
        Assert.Equal(0, selected);
        Assert.Null(problem);
    }

    [Fact]
    public void TabsComeBackInOrderWithSelection()
    {
        new OpenTabsFile(_folder).Save([new("comeback146", "Странник"), new("comeback146", null), new("pwclassic136", "Ёжик")], 2);

        var (tabs, selected) = new OpenTabsFile(_folder).Load(out _);

        Assert.Equal([new OpenTab("comeback146", "Странник"), new OpenTab("comeback146", null), new OpenTab("pwclassic136", "Ёжик")], tabs);
        Assert.Equal(2, selected);
    }

    [Fact]
    public void SelectionOutsideListFallsBackToLastTab()
    {
        File.WriteAllText(Path.Combine(_folder, OpenTabsFile.FileName),
            """{ "Tabs": [ { "Server": "comeback146", "Nick": "Странник" }, { "Server": "", "Nick": "без сервера" } ], "Selected": 5 }""");

        var (tabs, selected) = new OpenTabsFile(_folder).Load(out _);

        Assert.Equal([new OpenTab("comeback146", "Странник")], tabs);
        Assert.Equal(0, selected);
    }

    [Fact]
    public void BrokenFileIsKeptAsBadAndGivesNoTabs()
    {
        File.WriteAllText(Path.Combine(_folder, OpenTabsFile.FileName), "{ не json");

        var (tabs, _) = new OpenTabsFile(_folder).Load(out var problem);

        Assert.Empty(tabs);
        Assert.NotNull(problem);
        Assert.True(File.Exists(Path.Combine(_folder, OpenTabsFile.FileName + ".bad")));
    }
}

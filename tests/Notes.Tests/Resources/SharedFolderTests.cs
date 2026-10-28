using System;
using System.IO;
using Notes.Core.Logging;
using Notes.Core.Resources;
using Xunit;

namespace Notes.Tests.Resources;

/// <summary>Общая папка в %AppData%: папка под прежним именем переносится один раз, новая не затирается.</summary>
public class SharedFolderTests : IDisposable
{
    private readonly string _appData = Path.Combine(Path.GetTempPath(), "notes-appdata-" + Guid.NewGuid().ToString("N"));

    public SharedFolderTests() => Directory.CreateDirectory(_appData);

    public void Dispose() => Directory.Delete(_appData, recursive: true);

    private string Old => Path.Combine(_appData, SharedFolder.OldName);
    private string New => Path.Combine(_appData, SharedFolder.Name);

    [Fact]
    public void OldFolderMovesWithFiles()
    {
        Directory.CreateDirectory(Old);
        File.WriteAllText(Path.Combine(Old, "resources.json"), "[]");

        Assert.Equal(New, SharedFolder.Prepare(_appData, NullLogger.Instance));
        Assert.True(File.Exists(Path.Combine(New, "resources.json")));
        Assert.False(Directory.Exists(Old));
    }

    [Fact]
    public void NewFolderIsKeptWhenBothExist()
    {
        Directory.CreateDirectory(Old);
        File.WriteAllText(Path.Combine(Old, "resources.json"), "old");
        Directory.CreateDirectory(New);
        File.WriteAllText(Path.Combine(New, "resources.json"), "new");

        Assert.Equal(New, SharedFolder.Prepare(_appData, NullLogger.Instance));
        Assert.Equal("new", File.ReadAllText(Path.Combine(New, "resources.json")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Old, "resources.json")));
    }

    [Fact]
    public void WithoutOldFolderNothingHappens()
    {
        Assert.Equal(New, SharedFolder.Prepare(_appData, NullLogger.Instance));
        Assert.False(Directory.Exists(New));
    }

    [Fact]
    public void LockedOldFolderStaysInUse()
    {
        Directory.CreateDirectory(Old);
        using var held = new FileStream(Path.Combine(Old, "resources.json"), FileMode.Create, FileAccess.Write, FileShare.None);

        Assert.Equal(Old, SharedFolder.Prepare(_appData, NullLogger.Instance));
        Assert.False(Directory.Exists(New));
    }
}

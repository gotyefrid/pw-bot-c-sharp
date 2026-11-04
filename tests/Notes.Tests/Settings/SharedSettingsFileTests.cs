using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Notes.Core.Settings;
using Xunit;

namespace Notes.Tests.Settings;

/// <summary>Один settings.json у нескольких копий бота: каждая пишет только своё, правки другой не затираются.</summary>
public class SharedSettingsFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "notes-shared-" + Guid.NewGuid().ToString("N"));

    public SharedSettingsFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string File => Path.Combine(_folder, "settings.json");

    [Fact]
    public void TwoCopiesKeepEachOthersChanges()
    {
        var first = new SettingsStore(File);
        var second = new SettingsStore(File);
        var a = first.Load(out _);
        var b = second.Load(out _);

        a.Connection.ServerId = "comeback146";
        first.Save(a);
        b.Connection.RenameWindows = false;
        b.Target.FarmRadius = 42;
        second.Save(b);

        var saved = new SettingsStore(File).Load(out _);
        Assert.Equal("comeback146", saved.Connection.ServerId);
        Assert.False(saved.Connection.RenameWindows);
        Assert.Equal(42, saved.Target.FarmRadius);
    }

    [Fact]
    public void SameFieldLastSaveWins()
    {
        var first = new SettingsStore(File);
        var second = new SettingsStore(File);
        var a = first.Load(out _);
        var b = second.Load(out _);

        a.Connection.LastCharacter = "Первый";
        first.Save(a);
        b.Connection.LastCharacter = "Второй";
        second.Save(b);

        Assert.Equal("Второй", new SettingsStore(File).Load(out _).Connection.LastCharacter);
    }

    // Пути к клиентам — словарь: у каждой копии свой сервер, оба пути остаются
    [Fact]
    public void ClientPathsMergeByServer()
    {
        var first = new SettingsStore(File);
        var second = new SettingsStore(File);
        var a = first.Load(out _);
        var b = second.Load(out _);

        a.Connection.ClientPaths["pwclassic136"] = @"C:\pw1\elementclient.exe";
        first.Save(a);
        b.Connection.ClientPaths["comeback146"] = @"C:\pw2\elementclient.exe";
        second.Save(b);

        var paths = new SettingsStore(File).Load(out _).Connection.ClientPaths;
        Assert.Equal(2, paths.Count);
    }

    // Не читали файл (новый персонаж, файл персонажа) — пишем своё целиком
    [Fact]
    public void WithoutLoadWritesWhole()
    {
        var a = new SettingsStore(File).Load(out _);
        a.Target.FarmRadius = 11;
        new SettingsStore(File).Save(a);

        var fresh = new BotSettings();
        fresh.Target.FarmRadius = 22;
        new SettingsStore(File).Save(fresh);

        Assert.Equal(22, new SettingsStore(File).Load(out _).Target.FarmRadius);
    }

    [Fact]
    public void MergeTakesOursOnlyWhereChanged()
    {
        var baseline = JObject.Parse("""{ "a": 1, "b": { "x": 1, "y": 1 }, "list": [1] }""");
        var disk = JObject.Parse("""{ "a": 2, "b": { "x": 2, "y": 1 }, "list": [1], "other": true }""");
        var ours = JObject.Parse("""{ "a": 1, "b": { "x": 1, "y": 3 }, "list": [1, 2] }""");

        var merged = SettingsStore.Merge(disk, baseline, ours);

        Assert.True(JToken.DeepEquals(JObject.Parse("""{ "a": 2, "b": { "x": 2, "y": 3 }, "list": [1, 2], "other": true }"""), merged));
    }
}

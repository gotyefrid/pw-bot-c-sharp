using System;
using System.IO;
using BotCH.Core.Logging;
using BotCH.Core.Settings;
using Xunit;

namespace BotCH.Tests.Settings;

/// <summary>Общие настройки и настройки персонажа на диске: смена персонажа, новый — копия общих, что куда сохраняется.</summary>
public class SettingsServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "botch-settings-" + Guid.NewGuid().ToString("N"));

    public SettingsServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private SettingsService Service() => new(_folder, NullLogger.Instance);

    [Fact]
    public void UntilCharacterIsKnownCurrentAreAppSettings()
    {
        var config = Service();

        Assert.Null(config.Character);
        Assert.Same(config.App, config.Current);
    }

    [Fact]
    public void NewCharacterStartsAsCopyOfAppSettingsAndIsRemembered()
    {
        var config = Service();
        config.App.Target.FarmRadius = 77;

        Assert.True(config.SwitchTo("Заметно"));

        Assert.Equal("Заметно", config.Character);
        Assert.NotSame(config.App, config.Current);
        Assert.Equal(77, config.Current.Target.FarmRadius);
        Assert.Equal("Заметно", Service().App.Connection.LastCharacter);
    }

    [Fact]
    public void SameCharacterAgainChangesNothing()
    {
        var config = Service();
        config.SwitchTo("Заметно");
        var current = config.Current;

        Assert.False(config.SwitchTo("Заметно"));
        Assert.Same(current, config.Current);
    }

    [Fact]
    public void CharacterSettingsGoToTheirOwnFile()
    {
        var config = Service();
        config.SwitchTo("Заметно");
        config.Current.Target.FarmRadius = 33;
        config.SaveCurrent();

        var again = Service();
        again.SwitchTo("Заметно");

        Assert.Equal(33, again.Current.Target.FarmRadius);
        Assert.NotEqual(33, again.App.Target.FarmRadius);
    }

    [Fact]
    public void NewCharacterHasUnfreezeOffEvenIfItWasOnForEveryone()
    {
        // Unfreeze пишет в игру — новый персонаж (а на другом сервере все новые) начинает с выключенным
        var config = Service();
        config.App.Connection.Unfreeze = true;
        config.App.Unfreeze = true;

        config.SwitchTo("Новый");

        Assert.False(config.Current.Unfreeze);
        var again = Service();
        again.SwitchTo("Новый");
        Assert.False(again.Current.Unfreeze);
    }

    [Fact]
    public void CharacterFromOldFileKeepsCommonUnfreeze()
    {
        // До переноса unfreeze был общим — у прежних персонажей ферма продолжает работать в фоне, как работала
        var config = Service();
        config.App.Connection.Unfreeze = true;
        config.SaveApp();
        Directory.CreateDirectory(Path.Combine(_folder, "characters"));
        File.WriteAllText(Path.Combine(_folder, "characters", "Купчихан.json"), "{ \"target\": { \"farmRadius\": 30 } }");

        var again = Service();
        again.SwitchTo("Купчихан");

        Assert.True(again.Current.Unfreeze);
        var third = Service();
        third.App.Connection.Unfreeze = false;
        third.SwitchTo("Купчихан");
        Assert.True(third.Current.Unfreeze); // своё уже записано в файл персонажа
    }

    [Fact]
    public void BeforeCharacterIsKnownSaveGoesToAppSettings()
    {
        var config = Service();
        config.Current.Target.FarmRadius = 44;

        config.SaveCurrent();

        Assert.Equal(44, Service().App.Target.FarmRadius);
    }
}

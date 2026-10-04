using System;
using System.IO;
using BotCH.Core.Settings;
using Xunit;

namespace BotCH.Tests.Settings;

public class CharacterSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "botch-chars-" + Guid.NewGuid().ToString("N"));
    private readonly CharacterSettings _characters;
    private readonly BotSettings _template = new();

    public CharacterSettingsTests()
    {
        _characters = new CharacterSettings(_directory);
        _template.Potions.HpPercent = 65;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void NewCharacterStartsFromTemplateAndGetsFile()
    {
        var settings = _characters.Load("Персонаж", _template, out var problem);

        Assert.Null(problem);
        Assert.Equal(65, settings.Potions.HpPercent);
        Assert.True(_characters.Exists("Персонаж"));
        Assert.EndsWith("Персонаж.json", _characters.PathFor("Персонаж"));
    }

    [Fact]
    public void TwoCharactersHaveOwnSettings()
    {
        var druid = _characters.Load("Персонаж", _template, out _);
        druid.Pet.Cage = 3;
        _characters.Save("Персонаж", druid);

        var warrior = _characters.Load("Воин", _template, out _);
        warrior.Pet.Enabled = false;
        _characters.Save("Воин", warrior);

        Assert.Equal(3, _characters.Load("Персонаж", _template, out _).Pet.Cage);
        Assert.True(_characters.Load("Персонаж", _template, out _).Pet.Enabled);
        Assert.False(_characters.Load("Воин", _template, out _).Pet.Enabled);
        Assert.Equal(1, _characters.Load("Воин", _template, out _).Pet.Cage);
    }

    [Fact]
    public void TemplateIsCopiedNotShared()
    {
        var settings = _characters.Load("Персонаж", _template, out _);
        settings.Target.MobNames.Add("Волк");

        Assert.Empty(_template.Target.MobNames);
    }

    [Fact]
    public void LaterTemplateChangesDoNotTouchExistingCharacter()
    {
        _characters.Load("Персонаж", _template, out _);
        _template.Potions.HpPercent = 10;

        Assert.Equal(65, _characters.Load("Персонаж", _template, out _).Potions.HpPercent);
    }

    [Theory]
    [InlineData("Ник:с*звёздочкой?", "Ник_с_звёздочкой_.json")]
    [InlineData("  Пробелы  ", "Пробелы.json")]
    public void UnsafeNickBecomesSafeFileName(string nick, string file)
    {
        Assert.Equal(file, Path.GetFileName(_characters.PathFor(nick)));
    }

    [Fact]
    public void OldFileWithoutNewFieldsLoads()
    {
        // Файл от прошлой версии бота: нет лута по спискам и перезарядки, есть неизвестное поле
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_characters.PathFor("Персонаж"), """{ "pet": { "cage": 2 }, "oldField": true }""");

        var settings = _characters.Load("Персонаж", _template, out var problem);

        Assert.Null(problem);
        Assert.Equal(2, settings.Pet.Cage);
        Assert.Equal(LootListMode.All, settings.Loot.ListMode);
    }

    [Fact]
    public void BrokenFileFallsBackToTemplate()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_characters.PathFor("Персонаж"), "{ сломано");

        var settings = _characters.Load("Персонаж", _template, out var problem);

        Assert.NotNull(problem);
        Assert.Equal(65, settings.Potions.HpPercent);
    }
}

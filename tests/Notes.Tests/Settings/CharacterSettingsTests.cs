using System;
using System.IO;
using Notes.Core.Settings;
using Xunit;

namespace Notes.Tests.Settings;

public class CharacterSettingsTests : IDisposable
{
    private const string Server = "pwclassic136";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "notes-chars-" + Guid.NewGuid().ToString("N"));
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
        var settings = _characters.Load(Server, "Персонаж", _template, out var problem);

        Assert.Null(problem);
        Assert.Equal(65, settings.Potions.HpPercent);
        Assert.True(_characters.Exists(Server, "Персонаж"));
        Assert.EndsWith(@"pwclassic136\Персонаж.json", _characters.PathFor(Server, "Персонаж"));
    }

    [Fact]
    public void TwoCharactersHaveOwnSettings()
    {
        var druid = _characters.Load(Server, "Персонаж", _template, out _);
        druid.Pet.Cage = 3;
        _characters.Save(Server, "Персонаж", druid);

        var warrior = _characters.Load(Server, "Воин", _template, out _);
        warrior.Pet.Enabled = false;
        _characters.Save(Server, "Воин", warrior);

        Assert.Equal(3, _characters.Load(Server, "Персонаж", _template, out _).Pet.Cage);
        Assert.True(_characters.Load(Server, "Персонаж", _template, out _).Pet.Enabled);
        Assert.False(_characters.Load(Server, "Воин", _template, out _).Pet.Enabled);
        Assert.Equal(1, _characters.Load(Server, "Воин", _template, out _).Pet.Cage);
    }

    [Fact]
    public void TemplateIsCopiedNotShared()
    {
        var settings = _characters.Load(Server, "Персонаж", _template, out _);
        settings.Target.MobNames.Add("Волк");

        Assert.Empty(_template.Target.MobNames);
    }

    [Fact]
    public void LaterTemplateChangesDoNotTouchExistingCharacter()
    {
        _characters.Load(Server, "Персонаж", _template, out _);
        _template.Potions.HpPercent = 10;

        Assert.Equal(65, _characters.Load(Server, "Персонаж", _template, out _).Potions.HpPercent);
    }

    [Theory]
    [InlineData("Ник:с*звёздочкой?", "Ник_с_звёздочкой_.json")]
    [InlineData("  Пробелы  ", "Пробелы.json")]
    public void UnsafeNickBecomesSafeFileName(string nick, string file)
    {
        Assert.Equal(file, Path.GetFileName(_characters.PathFor(Server, nick)));
    }

    [Fact]
    public void OldFileWithoutNewFieldsLoads()
    {
        // Файл от прошлой версии бота: нет лута по спискам и перезарядки, есть неизвестное поле
        Directory.CreateDirectory(Path.Combine(_directory, Server));
        File.WriteAllText(_characters.PathFor(Server, "Персонаж"), """{ "pet": { "cage": 2 }, "oldField": true }""");

        var settings = _characters.Load(Server, "Персонаж", _template, out var problem);

        Assert.Null(problem);
        Assert.Equal(2, settings.Pet.Cage);
        Assert.Equal(LootListMode.All, settings.Loot.ListMode);
    }

    [Fact]
    public void BrokenFileFallsBackToTemplate()
    {
        Directory.CreateDirectory(Path.Combine(_directory, Server));
        File.WriteAllText(_characters.PathFor(Server, "Персонаж"), "{ сломано");

        var settings = _characters.Load(Server, "Персонаж", _template, out var problem);

        Assert.NotNull(problem);
        Assert.Equal(65, settings.Potions.HpPercent);
    }

    [Fact]
    public void SameNickOnOtherServerIsOtherCharacter()
    {
        var here = _characters.Load(Server, "Персонаж", _template, out _);
        here.Pet.Cage = 3;
        _characters.Save(Server, "Персонаж", here);

        Assert.Equal(1, _characters.Load("comeback146", "Персонаж", _template, out _).Pet.Cage);
        Assert.Equal(3, _characters.Load(Server, "Персонаж", _template, out _).Pet.Cage);
    }

    // Файл от прошлых версий — characters\Ник.json без сервера: переезжает к серверу, где персонаж подключился первым
    [Fact]
    public void OldFileMovesToServerFolder()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_characters.OldPathFor("Персонаж"), """{ "pet": { "cage": 2 } }""");

        Assert.True(_characters.MoveOld(Server, "Персонаж"));

        Assert.False(File.Exists(_characters.OldPathFor("Персонаж")));
        Assert.Equal(2, _characters.Load(Server, "Персонаж", _template, out _).Pet.Cage);
        Assert.False(_characters.MoveOld("comeback146", "Персонаж"));
    }

    [Fact]
    public void OldFileDoesNotOverwriteServerFile()
    {
        var here = _characters.Load(Server, "Персонаж", _template, out _);
        here.Pet.Cage = 3;
        _characters.Save(Server, "Персонаж", here);
        File.WriteAllText(_characters.OldPathFor("Персонаж"), """{ "pet": { "cage": 2 } }""");

        Assert.False(_characters.MoveOld(Server, "Персонаж"));

        Assert.Equal(3, _characters.Load(Server, "Персонаж", _template, out _).Pet.Cage);
    }
}

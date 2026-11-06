using System;
using System.IO;
using Notes.Core.Clients;
using Xunit;

namespace Notes.Tests.Clients;

public class CharacterBookTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "notes-book-" + Guid.NewGuid().ToString("N"));
    private readonly CharacterBook _book;

    public CharacterBookTests() => _book = new CharacterBook(Path.Combine(_directory, CharacterBook.FileName));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static CharacterAccount Druid(string nick = "Друид") => new()
    {
        Server = "pwclassic136", Nick = nick, Login = "login", Password = PasswordVault.Protect("секрет 1"),
    };

    [Fact]
    public void NoFileIsEmptyList()
    {
        Assert.Empty(_book.Load(out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void SavedCharacterComesBack()
    {
        var druid = Druid();
        druid.Comment = "Фарм пчёл\nкачать до 60";
        _book.Save(druid);

        var loaded = Assert.Single(new CharacterBook(_book.Path).Load(out _));
        Assert.Equal(druid.Id, loaded.Id);
        Assert.Equal("Друид", loaded.Nick);
        Assert.Equal("Фарм пчёл\nкачать до 60", loaded.Comment);
        Assert.Equal("секрет 1", PasswordVault.Unprotect(loaded.Password));
    }

    [Fact]
    public void PasswordIsNotStoredAsText()
    {
        _book.Save(Druid());

        var text = File.ReadAllText(_book.Path);
        Assert.DoesNotContain("секрет", text);
        Assert.Contains("dpapi:", text);
    }

    [Fact]
    public void EditReplacesByIdAndRemoveDeletes()
    {
        var druid = Druid();
        _book.Save(druid);
        _book.Save(Druid("Второй"));

        druid.Nick = "Новый";
        var list = _book.Save(druid);
        Assert.Equal(2, list.Count);
        Assert.Contains(list, a => a.Id == druid.Id && a.Nick == "Новый");

        list = _book.Remove(druid.Id);
        Assert.Equal("Второй", Assert.Single(list).Nick);
    }

    [Fact]
    public void TwoBotCopiesDoNotLoseEachOthersCharacters()
    {
        // Две копии бота из одной папки: каждая правка перечитывает файл
        var other = new CharacterBook(_book.Path);
        _book.Save(Druid("Первый"));
        other.Save(Druid("Второй"));

        Assert.Equal(2, _book.Load(out _).Count);
    }

    [Fact]
    public void SeenRemembersClassAndLevel()
    {
        _book.Save(Druid());
        var now = new DateTime(2026, 11, 6, 12, 0, 0);

        Assert.True(_book.Seen("pwclassic136", "Друид", 3, 45, now));
        Assert.False(_book.Seen("pwclassic136", "Друид", 3, 45, now.AddMinutes(1)));
        // Класс не прочитался — прежний остаётся
        Assert.True(_book.Seen("pwclassic136", "Друид", null, 46, now.AddMinutes(2)));

        var druid = Assert.Single(_book.Load(out _));
        Assert.Equal(3, druid.Class);
        Assert.Equal(46, druid.Level);
        Assert.Equal(now.AddMinutes(2), druid.SeenAt);
    }

    [Fact]
    public void SeenIgnoresOtherServerAndUnknownNick()
    {
        _book.Save(Druid());

        Assert.False(_book.Seen("comeback136", "Друид", 3, 45, DateTime.Now));
        Assert.False(_book.Seen("pwclassic136", "Чужой", 3, 45, DateTime.Now));
        Assert.Equal(0, Assert.Single(_book.Load(out _)).Level);
    }

    [Fact]
    public void BrokenFileIsKeptAsBad()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(_book.Path, "{ не json");

        Assert.Empty(_book.Load(out var problem));
        Assert.NotNull(problem);
        Assert.True(File.Exists(_book.Path + ".bad"));
    }

    [Fact]
    public void LaunchAddsLoginPasswordAndNick()
    {
        var exe = Path.Combine(_directory, "client.exe");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(exe, "");

        var launch = Druid().Launch(["startbypatcher", "game:cpw", "console:1"], exe);

        Assert.Equal(exe, launch.ExePath);
        Assert.Equal(_directory, launch.WorkingDirectory);
        Assert.Equal(["startbypatcher", "game:cpw", "console:1", "user:login", "pwd:секрет 1", "role:Друид"], launch.Arguments);
    }

    [Fact]
    public void LaunchExplainsWhatIsMissing()
    {
        var exe = Path.Combine(_directory, "client.exe");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(exe, "");
        string[] arguments = ["startbypatcher"];

        Assert.Contains("Не выбран клиент", Assert.Throws<InvalidOperationException>(() => Druid().Launch(arguments, null)).Message);
        Assert.Contains("не настроен", Assert.Throws<InvalidOperationException>(() => Druid().Launch([], exe)).Message);
        var noPassword = Druid();
        noPassword.Password = "";
        Assert.Contains("Пароль", Assert.Throws<InvalidOperationException>(() => noPassword.Launch(arguments, exe)).Message);
    }
}

using System;
using System.IO;
using System.Text;
using Notes.Core.Clients;
using Xunit;

namespace Notes.Tests.Clients;

/// <summary>Разбор файла запуска клиента: bat в UTF-8 и 1251, кавычки, ярлык читается отдельно на живом клиенте.</summary>
public class LaunchFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "notes-launch-" + Guid.NewGuid().ToString("N"));
    private readonly string _element;

    public LaunchFileTests()
    {
        _element = Path.Combine(_dir, "element");
        Directory.CreateDirectory(_element);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private string PutBat(string name, string text, Encoding encoding)
    {
        var path = Path.Combine(_element, name);
        File.WriteAllBytes(path, encoding.GetBytes(text));
        return path;
    }

    [Fact]
    public void Utf8BatKeepsCyrillicRole()
    {
        var path = PutBat("Тихоня 136.bat",
            "@echo off\r\nstart \"\" \"ElementClient.exe\" startbypatcher game:cpw console:1 user:login1 pwd:secret role:Тихоня\r\n",
            new UTF8Encoding(false));

        var launch = LaunchFile.Read(path);

        Assert.Equal(Path.Combine(_element, "ElementClient.exe"), launch.ExePath);
        Assert.Equal(_element, launch.WorkingDirectory);
        Assert.Equal("login1", launch.Login);
        Assert.Equal("Тихоня", launch.Role);
        Assert.True(launch.HasPassword);
        Assert.Equal(
            ["startbypatcher", "game:cpw", "console:1", "user:login1", "pwd:secret", "role:Тихоня"],
            launch.Arguments);
        Assert.DoesNotContain("secret", launch.ToString());
        Assert.Contains("Тихоня", launch.ToString());
    }

    [Fact]
    public void Cp1251BatKeepsCyrillicRole()
    {
        var path = PutBat("main_pers.bat", "start ElementClient.exe startbypatcher user:u pwd:p role:Мелок", Encoding.GetEncoding(1251));

        var launch = LaunchFile.Read(path);

        Assert.Equal("Мелок", launch.Role);
        Assert.Equal("ElementClient.exe", Path.GetFileName(launch.ExePath));
    }

    [Fact]
    public void QuotedExeAndRoleWithSpaceAreRead()
    {
        var path = PutBat("space.bat",
            "start \"\" \"C:\\Games\\PW 136\\ElementClient.exe\" startbypatcher user:u pwd:p \"role:Мой Перс\"",
            new UTF8Encoding(false));

        var launch = LaunchFile.Read(path);

        Assert.Equal(@"C:\Games\PW 136\ElementClient.exe", launch.ExePath);
        Assert.Equal("Мой Перс", launch.Role);
    }

    [Fact]
    public void StartTitleIsSkipped()
    {
        var path = PutBat("title.bat", "start \"Мой клиент\" ElementClient.exe startbypatcher user:u pwd:p role:Роль",
            new UTF8Encoding(false));

        var launch = LaunchFile.Read(path);

        Assert.Equal(Path.Combine(_element, "ElementClient.exe"), launch.ExePath);
        Assert.Equal("Роль", launch.Role);
    }

    [Fact]
    public void BatWithoutClientLaunchIsRejected()
    {
        var path = PutBat("empty.bat", "@echo off\r\nrem ничего не запускаем\r\n", new UTF8Encoding(false));

        var error = Assert.Throws<InvalidDataException>(() => LaunchFile.Read(path));

        Assert.Contains("нет команды запуска", error.Message);
    }

    [Fact]
    public void CredentialsCanBePassedWithoutFile()
    {
        var launch = ClientLaunch.WithCredentials(@"D:\Game\ElementClient.exe", @"D:\Game",
            ["startbypatcher", "console:1"], "user1", "secret", "Мой Перс");

        Assert.Equal(@"D:\Game\ElementClient.exe", launch.ExePath);
        Assert.Equal(@"D:\Game", launch.WorkingDirectory);
        Assert.Equal("user1", launch.Login);
        Assert.Equal("Мой Перс", launch.Role);
        Assert.Equal(["startbypatcher", "console:1", "user:user1", "pwd:secret", "role:Мой Перс"], launch.Arguments);
        Assert.DoesNotContain("secret", launch.ToString());
    }

    [Fact]
    public void ArgumentsLineQuotesSpacesOnly()
    {
        Assert.Equal("role:Тихоня", ClientLaunch.Quote("role:Тихоня"));
        Assert.Equal("\"role:Мой Перс\"", ClientLaunch.Quote("role:Мой Перс"));
        Assert.Equal("\"\"", ClientLaunch.Quote(""));
        Assert.Equal("startbypatcher user:u", ClientLaunch.ArgumentsLine(["startbypatcher", "user:u"]));
    }
}

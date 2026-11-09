using System;
using System.IO;
using Notes.Core.Calls;
using Notes.Core.GameFiles;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.GameFiles;

/// <summary>Строки окон игры из interfaces\ingame.stf и строка «Вы говорите с …» по шаблону оттуда.</summary>
public class UiStringsTests
{
    // Тест на файлах настоящего клиента: папка element — в переменной окружения. Нет переменной — тест пропускается
    private const string ComebackVariable = "NOTES_COMEBACK146_DIR";
    private static readonly string ComebackDirectory = Environment.GetEnvironmentVariable(ComebackVariable) ?? "";

    [Fact]
    public void ReadsNumberedLines()
    {
        var strings = UiStrings.Parse("1\t\"Заголовок\"\r\n199\t\"Осталось: %d:%02d\\r\"\r\n233\t\"Вы говорите с &%s&: %s\"\r\nмусор\r\n");

        Assert.Equal(3, strings.Count);
        Assert.Equal("Вы говорите с &%s&: %s", strings.Get(233));
        Assert.Equal("Осталось: %d:%02d\\r", strings.Get(199));
        Assert.Null(strings.Get(2));
    }

    [Fact]
    public void WhisperLineLikeChatWindow()
    {
        Assert.Equal("Вы говорите с &Друг&: привет", GameCaller.FormatChatLine("Вы говорите с &%s&: %s", "Друг", "привет"));
        // ^ и & — разметка игры: перед ними ^, как у окна чата
        Assert.Equal("Вы говорите с &Друг&: a ^& b ^^ 100%", GameCaller.FormatChatLine("Вы говорите с &%s&: %s", "Друг", "a & b ^ 100%"));
        Assert.Equal("50% у &Друг&", GameCaller.FormatChatLine("50%% у &%s&", "Друг"));
    }

    [Theory]
    [InlineData("Вы говорите с &%s&")]
    [InlineData("&%s& %d: %s")]
    [InlineData("%s %s %s")]
    public void WrongTemplateGivesNothing(string template)
        => Assert.Null(GameCaller.FormatChatLine(template, "Друг", "текст"));

    [Fact]
    public void MissingFileIsReported()
    {
        Assert.Null(UiStrings.LoadFromGameDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), out var problem));
        Assert.Contains("ingame.stf", problem);
    }

    [Fact]
    public void RealClientHasWhisperSentLine()
    {
        Assert.SkipUnless(Directory.Exists(ComebackDirectory), $"Не задана {ComebackVariable} — папка element клиента Comeback 1.4.6");

        var strings = UiStrings.LoadFromGameDirectory(ComebackDirectory, out var problem);
        var id = new ProfileCatalog().Load("comeback146").Data.Ui.WhisperSentText;

        Assert.Null(problem);
        Assert.Equal("Вы говорите с &%s&: %s", strings!.Get(id));
    }
}

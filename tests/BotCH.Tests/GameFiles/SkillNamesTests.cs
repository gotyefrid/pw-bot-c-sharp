using System.IO;
using BotCH.Core.GameFiles;
using Xunit;

namespace BotCH.Tests.GameFiles;

public class SkillNamesTests
{
    // Клиент владельца. Нет на машине — тест пропускается
    private const string GameDirectory = @"C:\Users\mel\Downloads\PWCLASSICNET[RU]\element";

    [Fact]
    public void ParseTakesOnlyNames()
    {
        var names = SkillNames.Parse("""
            2990  "Жалящий рой"
            2991  "Описание
            в две строки"
            3300  "Исцеление питомца"
            """);

        Assert.Equal(2, names.Count);
        Assert.Equal("Жалящий рой", names[299]);
        Assert.Equal("Исцеление питомца", names[330]);
    }

    [Fact]
    public void MissingDirectoryGivesEmptyWithReason()
    {
        var names = SkillNames.LoadFromGameDirectory(Path.Combine(Path.GetTempPath(), "нет-такой-папки"), out var problem);

        Assert.Equal(0, names.Count);
        Assert.NotNull(problem);
    }

    [Fact]
    public void RealConfigsPck()
    {
        Assert.SkipUnless(Directory.Exists(GameDirectory), "Клиента игры нет на этой машине");

        var names = SkillNames.LoadFromGameDirectory(GameDirectory, out var problem);

        Assert.Null(problem);
        Assert.True(names.Count > 1000);
        Assert.Equal("Жалящий рой", names.Get(299));
        Assert.Equal("Городской портал", names.Get(167));
        Assert.Equal("Оживление питомца", names.Get(329));
    }
}

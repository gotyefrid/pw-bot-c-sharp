using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.GameFiles;
using Notes.Core.Profiles;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.GameFiles;

public class ItemAddonTextsTests
{
    // Кусок configs\item_ext_prop.txt Comeback 1.3.6: комментарии и блоки «type: N { номера }»
    private const string ExtProp = """
        /*  Element 附加属性表
         3: 魔法攻击
        }
        */
        //  3: 魔法攻击
        type: 3
        {
        	815, 889,
        }

        // 11: 吟唱时间
        type: 11
        {
        	441, 442,
        }
        type: 54
        {
        	1273,
        }
        """;

    private static ItemAddonTexts Comeback136()
        => new(ItemAddonTexts.ParseTypes(ExtProp), new ProfileCatalog().Load("comeback136").Data.GameFiles.AddonTexts);

    [Fact]
    public void TypesAreReadFromBlocksNotComments()
    {
        var types = ItemAddonTexts.ParseTypes(ExtProp);

        Assert.Equal(new Dictionary<int, int> { [815] = 3, [889] = 3, [441] = 11, [442] = 11, [1273] = 54 }, types);
    }

    [Fact]
    public void SwordIsDescribedLikeGameTooltip()
    {
        // Меч владельца, подсказка в игре: «Время активации -3%», «Магическая атака +29», «Уменьшение физического урона +2%»
        var texts = Comeback136();

        Assert.Equal(["Время активации -3%", "Магическая атака +29", "Уменьшение физического урона +2%"],
            new[] { new ItemAddon(441, [3]), new ItemAddon(815, [29]), new ItemAddon(1273, [2]) }.Select(texts.Describe));
    }

    [Fact]
    public void UnknownAddonShowsNumberAndValues()
    {
        Assert.Equal("свойство 99999: 5/7", Comeback136().Describe(new ItemAddon(99999, [5, 7])));
        Assert.Equal("свойство 815: 29", ItemAddonTexts.Empty.Describe(new ItemAddon(815, [29])));
    }

    [Theory]
    [InlineData("Физическая атака %+d", "p0", new[] { 11 }, "Физическая атака +11")]
    [InlineData("Время активации %+d%%", "-p0", new[] { 3 }, "Время активации -3%")]
    [InlineData("Защита %+d, Физическая атака %+d", "p0,-p1", new[] { 5, 2 }, "Защита +5, Физическая атака -2")]
    // Восстановление маны: клиент делит число пополам нацело (бриджи владельца: в памяти 4, в подсказке +2)
    [InlineData("Восстановление маны %+d", "p0/2", new[] { 4 }, "Восстановление маны +2")]
    [InlineData("Восстановление маны %+d", "p0/2", new[] { 5 }, "Восстановление маны +2")]
    [InlineData("Пауза между атаками %.2f сек", "-p0*0.05", new[] { 2 }, "Пауза между атаками -0.10 сек")]
    // 0.03f — так клиент хранит дробные числа (биты float)
    [InlineData("Прочность %+d%%", "p0f*100", new[] { 0x3CF5C28F }, "Прочность +3%")]
    [InlineData("Дальность %+.2f", "p0f", new[] { 0x3F000000 }, "Дальность +0.50")]
    public void ValuesAreSubstitutedTheClientWay(string format, string args, int[] values, string expected)
    {
        Assert.Equal(expected, ItemAddonTexts.Format(format, args.Split(','), values));
    }

    [Fact]
    public void MissingValueGivesNoText()
    {
        Assert.Null(ItemAddonTexts.Format("Защита %+d, Физическая атака %+d", ["p0", "-p1"], [5]));
    }

    [Theory]
    [InlineData("comeback136")]
    [InlineData("comeback146")]
    [InlineData("pwclassic136")]
    public void EveryProfileTextHasAsManyNumbersAsPlaceholders(string id)
    {
        // Ошибка в таблице профиля видна сразу: на каждое %d / %f — свой способ получить число
        var texts = new ProfileCatalog().Load(id).Data.GameFiles.AddonTexts;
        Assert.NotEmpty(texts);
        foreach (var pair in texts)
        {
            var (type, text) = (pair.Key, pair.Value);
            var values = Enumerable.Repeat(1, 3).ToArray();
            Assert.True(ItemAddonTexts.Format(text[0], text.Skip(1).ToList(), values) is { } done && !System.Text.RegularExpressions.Regex.IsMatch(done, @"%[+.\d]*[df]"),
                $"тип {type}: «{text[0]}» с {string.Join(",", text.Skip(1))}");
        }
    }
}

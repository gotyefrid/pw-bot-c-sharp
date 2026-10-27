using System;
using System.IO;
using System.Linq;
using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.Profiles;

public class ProfileCatalogTests
{
    private readonly ProfileCatalog _catalog = new();

    [Fact]
    public void PwClassicIsEmbedded()
    {
        Assert.Contains("pwclassic136", _catalog.Ids);
    }

    [Fact]
    public void PwClassicKeyFacts()
    {
        // Клиент client.exe — ветки Comeback: перс +0x2C от game (у Comeback +0x28)
        var data = _catalog.Load("pwclassic136").Data;

        Assert.Equal("PW Classic 1.3.6", data.Name);
        Assert.Equal(0x1Cu, data.Base.Game);
        Assert.Equal(0x2Cu, data.Host.Struct);
        Assert.Equal(0x2B8u, data.Npc.State);
        Assert.Equal(769, data.World.SlotCount);
        Assert.Equal(0x10u, data.Skill.CooldownLeft);
        Assert.Equal(20, data.PetManager.CageCount);
        Assert.Equal(299, data.Skills.DefaultAttack);
        Assert.Equal([167, 329, 330], data.Skills.NotAttack);
    }

    [Theory]
    [InlineData("pwclassic136")]
    [InlineData("comeback136")]
    [InlineData("comeback146")]
    public void AllFunctionsHaveAddressAndLongSignature(string id)
    {
        var functions = _catalog.Load(id).Data.Functions;

        Assert.All(functions, f =>
        {
            Assert.NotEqual(0u, f.Value.Rva);
            Assert.NotNull(f.Value.Signature);
            Assert.True(f.Value.Signature!.FixedCount >= 10, $"{f.Key}: слишком короткая сигнатура");
        });
    }

    [Theory]
    [InlineData("pwclassic136")]
    [InlineData("comeback136")]
    [InlineData("comeback146")]
    public void EveryServerCanDoTheSame(string id)
    {
        // Бот на всех серверах умеет одно и то же: одинаковые функции, все возможности
        var profile = _catalog.Load(id);

        Assert.Equal(_catalog.Load("comeback136").Data.Functions.Keys.OrderBy(k => k), profile.Data.Functions.Keys.OrderBy(k => k));
        Assert.All(Enum.GetValues(typeof(Capability)).Cast<Capability>(), c => Assert.Null(profile.Capabilities.WhyNot(c)));
        // Группы лута — данные игры, одни на всех; их названия хранятся в настройках персонажа
        Assert.NotEmpty(profile.Data.LootGroups);
        Assert.Equal(_catalog.Load("comeback136").Data.LootGroups.Select(g => (g.Type, g.Name)), profile.Data.LootGroups.Select(g => (g.Type, g.Name)));
    }

    [Fact]
    public void ComebackHasOnlyWhatWasFound()
    {
        var profile = _catalog.Load("comeback146");

        Assert.Equal("Comeback 1.4.6", profile.Name);
        Assert.Equal(0x20u, profile.Data.World.Npcs.SlotArray);
        // Выбор цели — метод персонажа: this берётся из памяти, «снять цель» — он же с нулём
        var select = profile.Data.Functions[GameFunctions.SelectTarget];
        Assert.Equal(FunctionThis.Host, select.This);
        Assert.Equal(["wid"], select.Args);
        Assert.Equal(["0"], profile.Data.Functions[GameFunctions.Unselect].Args);
        Assert.True(profile.Capabilities.Has(Capability.SmartMove));
        Assert.True(profile.Capabilities.Has(Capability.RecallPet));
    }

    [Fact]
    public void Comeback136IsPortedFrom146()
    {
        // Та же ветка клиента, что 1.4.6, другая сборка: все функции 1.4.6 на новых адресах, у перса сдвинуты поля
        var p136 = _catalog.Load("comeback136").Data;
        var p146 = _catalog.Load("comeback146").Data;

        Assert.Equal("Comeback 1.3.6", _catalog.Load("comeback136").Name);
        Assert.Equal(0x5A8u, p136.Host.TargetId);
        Assert.Equal(0x790u, p136.Host.MoveEnv);
        Assert.Equal(p146.Npc.State, p136.Npc.State);
    }

    [Fact]
    public void UnknownServerIsReported()
    {
        var error = Assert.Throws<ArgumentException>(() => _catalog.Load("nope"));
        Assert.Contains("pwclassic136", error.Message);
    }

    [Fact]
    public void FileNextToExeOverridesEmbedded()
    {
        var directory = Path.Combine(Path.GetTempPath(), "botch-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "pwclassic136.jsonc"),
                """
                // переопределённый профиль
                { "id": "pwclassic136", "name": "Тест", "base": { "game": "0x123" } }
                """);

            var data = new ProfileCatalog(directory).Load("pwclassic136").Data;

            Assert.Equal("Тест", data.Name);
            Assert.Equal(0x123u, data.Base.Game);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public class ProfileJsonTests
{
    [Fact]
    public void HexAndDecimalNumbersAreAccepted()
    {
        var data = ProfileJson.Parse("""{ "host": { "hp": "0x46C", "mp": 1136, "level": "1124" } }""");

        Assert.Equal(0x46Cu, data.Host.Hp);
        Assert.Equal(1136u, data.Host.Mp);
        Assert.Equal(1124u, data.Host.Level);
    }

    [Fact]
    public void TypoInFieldNameIsAnError()
    {
        // Опечатка не должна молча превращаться в смещение 0
        Assert.ThrowsAny<Exception>(() => ProfileJson.Parse("""{ "host": { "hpp": "0x46C" } }"""));
    }

    [Fact]
    public void BadHexIsAnError()
    {
        Assert.ThrowsAny<Exception>(() => ProfileJson.Parse("""{ "host": { "hp": "0xZZ" } }"""));
    }

    [Fact]
    public void FunctionIsParsed()
    {
        var data = ProfileJson.Parse("""
            { "functions": { "selectTarget": { "rva": "0x1F0330", "convention": "cdecl", "signature": "56 6A 06 E8 ?? ?? ?? ??" } } }
            """);

        var function = data.Functions[GameFunctions.SelectTarget];
        Assert.Equal(0x1F0330u, function.Rva);
        Assert.Equal("56 6A 06 E8 ?? ?? ?? ??", function.Signature!.ToString());
    }

    [Fact]
    public void SerializeAndParseAgain()
    {
        var original = new ProfileCatalog().Load("pwclassic136").Data;

        var copy = ProfileJson.Parse(ProfileJson.Serialize(original));

        Assert.Equal(original.Host.TargetId, copy.Host.TargetId);
        Assert.Equal(original.Functions.Keys.OrderBy(k => k), copy.Functions.Keys.OrderBy(k => k));
        Assert.Equal(original.Functions[GameFunctions.WorkStart].Signature!.ToString(), copy.Functions[GameFunctions.WorkStart].Signature!.ToString());
    }
}

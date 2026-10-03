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
    public void PwClassicKeyFactsMatchOldBot()
    {
        var data = _catalog.Load("pwclassic136").Data;

        Assert.Equal("PW Classic 1.3.6", data.Name);
        Assert.Equal(0x5B3EECu, data.Base.BasePointer);
        Assert.Equal(0x1Cu, data.Base.Game);
        Assert.Equal(0x20u, data.Host.Struct);
        Assert.Equal(0x2B8u, data.Npc.State);
        Assert.Equal(0x2D4u, data.Npc.Target);
        Assert.Equal(769, data.World.SlotCount);
        Assert.Equal(0x10u, data.Skill.CooldownLeft);
        Assert.Equal(10, data.PetManager.CageCount);
        Assert.Equal(299, data.Skills.DefaultAttack);
        Assert.Equal([167, 329, 330], data.Skills.NotAttack);
    }

    [Fact]
    public void AllPwClassicFunctionsHaveAddressAndLongSignature()
    {
        var functions = _catalog.Load("pwclassic136").Data.Functions;

        Assert.Equal(16, functions.Count);
        Assert.All(functions, f =>
        {
            Assert.NotEqual(0u, f.Value.Rva);
            Assert.NotNull(f.Value.Signature);
            Assert.True(f.Value.Signature!.FixedCount >= 10, $"{f.Key}: слишком короткая сигнатура");
        });
        Assert.Equal(CallingConvention.Thiscall, functions[GameFunctions.HostApplySkill].Convention);
        Assert.Equal(CallingConvention.Cdecl, functions[GameFunctions.SelectTarget].Convention);
    }

    [Fact]
    public void NoForbiddenFunctionsInProfile()
    {
        // 0x5F1FC0 — отпустить пета (c2s 0x66). Выход из игры (c2s 0x01) тоже не должен попасть в профиль
        var functions = _catalog.Load("pwclassic136").Data.Functions.Values;

        Assert.DoesNotContain(functions, f => f.Rva == 0x1F1FC0);
    }

    [Fact]
    public void PwClassicHasAllCapabilities()
    {
        var capabilities = _catalog.Load("pwclassic136").Capabilities;

        Assert.True(capabilities.DirectCalls);
        Assert.True(capabilities.ApproachLikeMouse);
        Assert.True(capabilities.MoveToPoint);
        Assert.True(capabilities.GroundItems);
    }

    [Fact]
    public void ComebackHasOnlyWhatWasFound()
    {
        var profile = _catalog.Load("comeback146");

        Assert.Equal("Comeback 1.4.6", profile.Name);
        Assert.Equal(0x8EBC1Cu, profile.Data.Base.BasePointer);
        Assert.Equal(0x20u, profile.Data.World.Npcs.SlotArray);
        // Выбор цели — метод персонажа: this берётся из памяти, «снять цель» — он же с нулём
        var select = profile.Data.Functions[GameFunctions.SelectTarget];
        Assert.Equal(FunctionThis.Host, select.This);
        Assert.Equal(["wid"], select.Args);
        Assert.Equal(["0"], profile.Data.Functions[GameFunctions.Unselect].Args);
        Assert.True(profile.Capabilities.DirectCalls);
        Assert.True(profile.Capabilities.GroundItems);
        // Выход из игры и «отпустить пета» запрещены с самого начала — до первой вызываемой функции
        Assert.Equal(0x445A20u, profile.Data.ForbiddenFunctions["logout"]);
        Assert.Equal(0x2D4EB0u, profile.Data.ForbiddenFunctions["releasePet"]);
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
                { "id": "pwclassic136", "name": "Тест", "base": { "basePointer": "0x123" } }
                """);

            var data = new ProfileCatalog(directory).Load("pwclassic136").Data;

            Assert.Equal("Тест", data.Name);
            Assert.Equal(0x123u, data.Base.BasePointer);
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

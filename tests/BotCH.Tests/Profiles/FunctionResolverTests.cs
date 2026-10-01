using System.Diagnostics;
using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.Profiles;

/// <summary>
/// Поиск функций на настоящем коде — коде самого процесса тестов (там есть PE-заголовок и секция .text).
/// </summary>
public class FunctionResolverTests
{
    private static GameProcess OpenSelf() => GameProcess.Open(Process.GetCurrentProcess().Id);

    [Fact]
    public void SectionsOfOwnExeIncludeCode()
    {
        using var process = OpenSelf();

        var sections = ModuleSections.Read(process, process.MainModuleBase);

        Assert.Contains(sections, s => s.IsCode && s.Size > 0);
    }

    [Fact]
    public void FoundAtProfileAddress()
    {
        using var process = OpenSelf();
        var (rva, signature) = UniqueCodeSample(process);

        var location = new FunctionResolver(process, process.MainModuleBase)
            .Resolve("test", new GameFunction { Rva = rva, Signature = signature });

        Assert.Equal(FunctionStatus.Found, location.Status);
        Assert.Equal(process.MainModuleBase + rva, location.Address);
    }

    [Fact]
    public void RelocatedWhenProfileAddressIsWrong()
    {
        using var process = OpenSelf();
        var (rva, signature) = UniqueCodeSample(process);

        var location = new FunctionResolver(process, process.MainModuleBase)
            .Resolve("test", new GameFunction { Rva = rva + 1, Signature = signature });

        Assert.Equal(FunctionStatus.Relocated, location.Status);
        Assert.Equal(process.MainModuleBase + rva, location.Address);
        Assert.True(location.IsUsable);
    }

    [Fact]
    public void NotFoundSignatureIsNotUsable()
    {
        using var process = OpenSelf();

        var location = new FunctionResolver(process, process.MainModuleBase)
            .Resolve("test", new GameFunction { Rva = 0x1000, Signature = Signature.Parse("DE AD BE EF 13 37 C0 DE BA AD F0 0D") });

        Assert.Equal(FunctionStatus.NotFound, location.Status);
        Assert.False(location.IsUsable);
    }

    [Fact]
    public void ShortCommonSignatureIsAmbiguous()
    {
        using var process = OpenSelf();
        var resolver = new FunctionResolver(process, process.MainModuleBase);
        var code = CodeSection(process);
        // Самый частый байт в коде: шаблон из одного байта встречается много раз
        var common = Enumerable.Range(0, code.Size).Select(i => process.ReadByte(code.Address + (uint)i)).Take(4096)
            .GroupBy(b => b).OrderByDescending(g => g.Count()).First().Key;

        var location = resolver.Resolve("test", new GameFunction { Rva = 1, Signature = Signature.Parse(common.ToString("X2")) });

        Assert.Equal(FunctionStatus.Ambiguous, location.Status);
        Assert.False(location.IsUsable);
    }

    [Fact]
    public void MissingFunctionIsNotInProfile()
    {
        using var process = OpenSelf();

        var location = new FunctionResolver(process, process.MainModuleBase).Resolve("test", null);

        Assert.Equal(FunctionStatus.NotInProfile, location.Status);
    }

    private static ModuleSection CodeSection(GameProcess process)
        => ModuleSections.Read(process, process.MainModuleBase).First(s => s.IsCode && s.Size > 0);

    // Кусок кода собственного exe, встречающийся в коде ровно один раз
    private static (uint Rva, Signature Signature) UniqueCodeSample(GameProcess process)
    {
        var section = CodeSection(process);
        var resolver = new FunctionResolver(process, process.MainModuleBase);
        var code = ModuleSections.ReadRegion(process, section.Address, section.Size);

        for (var offset = 0; offset + 32 <= code.Length; offset += 16)
        {
            var bytes = code.Skip(offset).Take(32).ToArray();
            if (bytes.Distinct().Count() < 8)
                continue;

            var signature = Signature.Parse(string.Join(" ", bytes.Select(b => b.ToString("X2"))));
            var hits = resolver.FindEverywhere(signature);
            if (hits.Count == 1)
                return (section.Address + (uint)offset - process.MainModuleBase, signature);
        }

        throw new Xunit.Sdk.XunitException("Не нашлось уникального куска кода");
    }
}

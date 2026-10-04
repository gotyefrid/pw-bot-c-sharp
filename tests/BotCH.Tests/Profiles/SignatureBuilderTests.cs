using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.Profiles;

public class SignatureBuilderTests
{
    private const uint ModuleBase = 0x400000;
    private const int ModuleSize = 0x67E000;

    // Начало c2s_SendCmdSummonPet из PW Classic 1.3.6 (0x5F1F40)
    private static readonly byte[] SummonPet =
    [
        0x56, 0x6A, 0x06, 0xE8, 0x08, 0x13, 0x17, 0x00, 0x8B, 0xF0, 0x83, 0xC4, 0x04, 0x85, 0xF6, 0x74,
        0x26, 0x8B, 0x44, 0x24, 0x08, 0x66, 0xC7, 0x06, 0x64, 0x00, 0x89, 0x46, 0x02, 0x8B, 0x0D, 0xEC,
        0x3E, 0x9B, 0x00, 0x6A, 0x06,
    ];

    [Fact]
    public void CallTargetIsWildcarded()
    {
        var signature = SignatureBuilder.FromCode(SummonPet, 8, ModuleBase, ModuleSize);

        Assert.Equal("56 6A 06 E8 ?? ?? ?? ??", signature.ToString());
    }

    [Fact]
    public void CommandNumberIsKept()
    {
        // «66 C7 06 64 00» — mov word [esi], 0x64: число 0x006406C7 похоже на адрес в модуле, но это номер команды
        var signature = SignatureBuilder.FromCode(SummonPet, 28, ModuleBase, ModuleSize);

        Assert.EndsWith("66 C7 06 64 00 89 46", signature.ToString());
    }

    [Fact]
    public void GlobalAddressIsWildcarded()
    {
        // «8B 0D EC 3E 9B 00» — mov ecx, [0x9B3EEC]
        var signature = SignatureBuilder.FromCode(SummonPet, 37, ModuleBase, ModuleSize);

        Assert.EndsWith("8B 0D ?? ?? ?? ?? 6A 06", signature.ToString());
    }

    [Fact]
    public void BuiltSignatureMatchesItsSource()
    {
        Assert.True(SignatureBuilder.FromCode(SummonPet, 37, ModuleBase, ModuleSize).Matches(SummonPet));
    }
}

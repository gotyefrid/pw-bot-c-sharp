using System;
using System.Linq;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.Profiles;

/// <summary>Адрес глобальной переменной — из кода, который с ней работает (как список чата в Comeback 1.4.6).</summary>
public class CodeDataSearchTests
{
    private const uint List = 0x00F7_43F0;

    // lea eax, [ebp-0x9AC]; mov ecx, список; push eax; call …; cmp [список + 0xC], 200; jl
    private static readonly Signature Find = Signature.Parse("8D 85 54 F6 FF FF B9 ?? ?? ?? ?? 50 E8 ?? ?? ?? ?? 81 3D ?? ?? ?? ?? C8 00 00 00 7C");

    private static byte[] AddMessage(uint list)
        => [0x8D, 0x85, 0x54, 0xF6, 0xFF, 0xFF, 0xB9, .. BitConverter.GetBytes(list), 0x50, 0xE8, 0xE4, 0x14, 0xE2, 0xFF,
            0x81, 0x3D, .. BitConverter.GetBytes(list + 0xC), 0xC8, 0x00, 0x00, 0x00, 0x7C, 0x2A];

    private static byte[] Noise(int length) => Enumerable.Repeat((byte)0xCC, length).ToArray();

    [Fact]
    public void AddressIsTakenFromTheCode()
    {
        byte[] code = [.. Noise(100), .. AddMessage(List), .. Noise(50)];

        Assert.Equal(List, CodeDataSearch.Find([(0x0068_1000u, code)], Find, 7));
    }

    [Fact]
    public void FoundInAnyCodeSection()
    {
        Assert.Equal(List, CodeDataSearch.Find([(0x0068_1000u, Noise(64)), (0x00D0_0000u, AddMessage(List))], Find, 7));
    }

    [Fact]
    public void NotFoundOrSeveralPlacesIsAnError()
    {
        Assert.Contains("не найден", Assert.Throws<InvalidOperationException>(() => CodeDataSearch.Find([(0u, Noise(64))], Find, 7)).Message);
        byte[] twice = [.. AddMessage(List), .. AddMessage(List + 0x10)];
        Assert.Contains("нескольких", Assert.Throws<InvalidOperationException>(() => CodeDataSearch.Find([(0u, twice)], Find, 7)).Message);
    }

    [Fact]
    public void AddressOutsideTheSignatureIsAnError()
    {
        Assert.Throws<InvalidOperationException>(() => CodeDataSearch.Find([(0u, AddMessage(List))], Find, 26));
    }
}

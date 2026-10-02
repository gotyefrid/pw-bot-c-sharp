using System;
using System.Linq;
using BotCH.Core.Calls;
using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.Calls;

/// <summary>
/// Байты заглушек сверяются с эталоном, снятым с настоящего старого GameCall (BuildStub / BuildMoveStub через рефлексию
/// из собранного BotCH.exe ветки master) — эти байты проверены в игре.
/// </summary>
public class StubBuilderTests
{
    private static string Hex(byte[] bytes) => string.Join(" ", bytes.Select(b => b.ToString("X2")));

    [Fact]
    public void CdeclOneArgument()
    {
        // c2s_SendCmdSelectTarget(0x80104298) @0x5F0330
        var stub = StubBuilder.Call(0x5F0330, CallingConvention.Cdecl, 0, [0x80104298]);

        Assert.Equal("68 98 42 10 80 B8 30 03 5F 00 FF D0 83 C4 04 31 C0 C2 04 00", Hex(stub));
    }

    [Fact]
    public void CdeclArgumentsArePushedRightToLeft()
    {
        // UseItem(where 0, slot 2, tid 8618, count 1)
        var stub = StubBuilder.Call(0x5F03F0, CallingConvention.Cdecl, 0, [0, 2, 8618, 1]);

        Assert.Equal(
            "68 01 00 00 00 68 AA 21 00 00 68 02 00 00 00 68 00 00 00 00 B8 F0 03 5F 00 FF D0 83 C4 10 31 C0 C2 04 00",
            Hex(stub));
    }

    [Fact]
    public void CdeclWithoutArgumentsHasNoStackCleanup()
    {
        Assert.Equal("B8 90 0C 5F 00 FF D0 31 C0 C2 04 00", Hex(StubBuilder.Call(0x5F0C90, CallingConvention.Cdecl, 0, [])));
    }

    [Fact]
    public void ThiscallPutsObjectInEcxAndDoesNotCleanStack()
    {
        // CECHostPlayer::ApplySkill(299, 0, 0, -1), this = перс
        var stub = StubBuilder.Call(0x45CC50, CallingConvention.Thiscall, 0x1FA1F868, [299, 0, 0, 0xFFFFFFFF]);

        Assert.Equal(
            "68 FF FF FF FF 68 00 00 00 00 68 00 00 00 00 68 2B 01 00 00 B9 68 F8 A1 1F B8 50 CC 45 00 FF D0 31 C0 C2 04 00",
            Hex(stub));
    }

    [Fact]
    public void StdcallNeitherCleansStackNorSetsEcx()
    {
        // Comeback 1.4.6: функция сама снимает аргумент (ret 4)
        var stub = StubBuilder.Call(0x841220, CallingConvention.Stdcall, 0, [0x80104298]);

        Assert.Equal("68 98 42 10 80 B8 20 12 84 00 FF D0 31 C0 C2 04 00", Hex(stub));
    }

    [Fact]
    public void RegisterArgumentsGoToEcxAndEdx()
    {
        // Comeback 1.4.6, «использовать предмет»: cl = откуда, dl = ячейка, в стеке tid и количество, стек чистит вызывающий
        var stub = StubBuilder.Call(0x7F6FD0, CallingConvention.Cdecl, 0, [8647, 1], ecx: 0, edx: 1);

        Assert.Equal(
            "68 01 00 00 00 68 C7 21 00 00 B9 00 00 00 00 BA 01 00 00 00 B8 D0 6F 7F 00 FF D0 83 C4 08 31 C0 C2 04 00",
            Hex(stub));
    }

    [Fact]
    public void ThiscallWithEcxArgumentIsRefused()
    {
        Assert.Throws<ArgumentException>(() => StubBuilder.Call(0x45CC50, CallingConvention.Thiscall, 0x1000, [1], ecx: 2));
    }

    [Fact]
    public void ThiscallWithoutObjectIsRefused()
    {
        Assert.Throws<ArgumentException>(() => StubBuilder.Call(0x45CC50, CallingConvention.Thiscall, 0, [1]));
    }

    [Fact]
    public void MoveMatchesOldStub()
    {
        var stub = StubBuilder.MoveTo(0x11223344, 0x466C70, 0x46A890, 0x467070, 0x55660100, 0);

        Assert.Equal(
            "56 B9 44 33 22 11 6A 01 B8 70 6C 46 00 FF D0 85 C0 74 25 8B F0 68 00 01 66 55 6A 00 8B CE B8 90 A8 46 00 FF D0 "
            + "6A 00 6A 01 56 6A 01 B9 44 33 22 11 B8 70 70 46 00 FF D0 5E 31 C0 C2 04 00",
            Hex(stub));
    }

    [Fact]
    public void MoveJumpLandsOnPopEsi()
    {
        var stub = StubBuilder.MoveTo(1, 2, 3, 4, 5, 0);
        var jz = Array.IndexOf(stub, (byte)0x74);

        Assert.Equal(0x5E, stub[jz + 2 + stub[jz + 1]]);
    }
}

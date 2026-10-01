using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BotCH.Core.Memory;
using Xunit;

namespace BotCH.Tests.Memory;

/// <summary>
/// Настоящий ReadProcessMemory/WriteProcessMemory, но на процессе самих тестов — игра не нужна.
/// </summary>
public class GameProcessTests
{
    private static readonly int OwnPid = Process.GetCurrentProcess().Id;

    [Fact]
    public void ReadsOwnMemory()
    {
        var data = new byte[] { 0x78, 0x56, 0x34, 0x12 };
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            using var process = GameProcess.Open(OwnPid);

            Assert.Equal(0x12345678u, process.ReadUInt32((uint)pin.AddrOfPinnedObject().ToInt64()));
        }
        finally
        {
            pin.Free();
        }
    }

    [Theory]
    [InlineData(0x80116200u)] // WID моба, по ошибке принятый за указатель
    [InlineData(0xFFFFFFF0u)]
    public void HighAddressIsNotReadableButDoesNotThrow(uint address)
    {
        using var process = GameProcess.Open(OwnPid);

        Assert.False(process.TryRead(address, new byte[4], 4));
    }

    [Fact]
    public void WritesOwnMemoryWhenAllowed()
    {
        var data = new byte[4];
        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            using var process = GameProcess.Open(OwnPid, GameProcessRights.Write);

            process.WriteUInt32((uint)pin.AddrOfPinnedObject().ToInt64(), 1);

            Assert.Equal(1u, BitConverter.ToUInt32(data, 0));
        }
        finally
        {
            pin.Free();
        }
    }

    [Fact]
    public void WriteIsRefusedForReadOnlyProcess()
    {
        using var process = GameProcess.Open(OwnPid);

        Assert.Throws<InvalidOperationException>(() => process.WriteUInt32(0x1000, 1));
    }

    [Fact]
    public void MainModuleStartsWithMz()
    {
        using var process = GameProcess.Open(OwnPid);

        Assert.NotEqual(0u, process.MainModuleBase);
        Assert.True(process.MainModuleSize > 0);
        Assert.Equal((ushort)0x5A4D, process.ReadUInt16(process.MainModuleBase)); // «MZ» — заголовок exe
    }

    [Fact]
    public void NullPageIsUnreadable()
    {
        using var process = GameProcess.Open(OwnPid);

        Assert.False(process.TryReadUInt32(0, out _));
    }
}

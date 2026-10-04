using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BotCH.Core.Calls;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using Xunit;

namespace BotCH.Tests.Calls;

/// <summary>
/// Настоящий запуск заглушки потоком — но в процессе самих тестов, не в игре. Заглушка вызывает функцию теста,
/// так проверяется вся цепочка: выделение памяти, запись данных и кода, поток, передача аргументов, освобождение.
/// </summary>
public class RemoteRunTests
{
    [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private delegate void Receiver(uint a, uint b, uint dataPointer);

    private static uint _a, _b, _dataValue;

    private static void Receive(uint a, uint b, uint dataPointer)
    {
        _a = a;
        _b = b;
        _dataValue = (uint)Marshal.ReadInt32(new IntPtr(unchecked((int)dataPointer)));
    }

    [Fact]
    public void StubCallsFunctionWithArgumentsAndData()
    {
        Receiver receiver = Receive;
        var function = unchecked((uint)Marshal.GetFunctionPointerForDelegate(receiver).ToInt32());
        using var self = GameProcess.Open(Process.GetCurrentProcess().Id, GameProcessRights.Execute);

        var result = new ThreadCallRunner(self).Run(BitConverter.GetBytes(0xC0FFEEu), data => StubBuilder.Call(function, BotCH.Core.Profiles.CallingConvention.Cdecl, 0, [111, 222, data]));

        Assert.True(result.IsDone, result.Details);
        Assert.Equal((111u, 222u, 0xC0FFEEu), (_a, _b, _dataValue));
        GC.KeepAlive(receiver);
    }

    [Fact]
    public void ReadOnlyProcessCannotRun()
    {
        using var self = GameProcess.Open(Process.GetCurrentProcess().Id);

        Assert.Throws<InvalidOperationException>(() => new ThreadCallRunner(self).Run(null, _ => [0xC3]));
    }

    [Fact]
    public void TooBigStubIsRefused()
    {
        using var self = GameProcess.Open(Process.GetCurrentProcess().Id, GameProcessRights.Execute);

        var result = new ThreadCallRunner(self).Run(null, _ => new byte[0x200]);

        Assert.Equal(RemoteRunStatus.Failed, result.Status);
    }
}

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Notes.Core.Calls;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Xunit;

namespace Notes.Tests.Calls;

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

        var result = new ThreadCallRunner(self).Run(BitConverter.GetBytes(0xC0FFEEu), data => StubBuilder.Call(function, Notes.Core.Profiles.CallingConvention.Cdecl, 0, [111, 222, data]));

        Assert.True(result.IsDone, result.Details);
        Assert.Equal((111u, 222u, 0xC0FFEEu), (_a, _b, _dataValue));
        GC.KeepAlive(receiver);
    }

    [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.ThisCall)]
    private delegate uint Checker(IntPtr self, uint task);

    // Как проверка задания: ответ зависит от объекта (this) и номера
    private static uint Check(IntPtr self, uint task) => task * 10 + (uint)self.ToInt32();

    [Fact]
    public void CallEachWritesAnswersBackIntoData()
    {
        Checker checker = Check;
        var function = unchecked((uint)Marshal.GetFunctionPointerForDelegate(checker).ToInt32());
        using var self = GameProcess.Open(Process.GetCurrentProcess().Id, GameProcessRights.Execute);
        var data = new byte[3 * 8];
        for (var i = 0; i < 3; i++)
            Buffer.BlockCopy(BitConverter.GetBytes((uint)(i + 1)), 0, data, i * 4, 4);

        var result = new ThreadCallRunner(self).Run(data, at => StubBuilder.CallEach(function, 7, at, at + 12, 3));

        Assert.True(result.IsDone, result.Details);
        Assert.Equal([17u, 27u, 37u], [BitConverter.ToUInt32(result.Data!, 12), BitConverter.ToUInt32(result.Data!, 16),
            BitConverter.ToUInt32(result.Data!, 20)]);
        GC.KeepAlive(checker);
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

        var result = new ThreadCallRunner(self).Run(null, _ => new byte[0x1001]);

        Assert.Equal(RemoteRunStatus.Failed, result.Status);
    }
}

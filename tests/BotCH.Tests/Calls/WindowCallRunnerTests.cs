using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using BotCH.Core.Calls;
using BotCH.Core.Memory;
using Xunit;

namespace BotCH.Tests.Calls;

/// <summary>
/// Настоящий обработчик окна — на своём окне в процессе тестов: установка потоком, вызов через сообщение (должен выполниться
/// в потоке окна, а не в отдельном), чужие сообщения доходят до прежнего обработчика, «Стоп» обработчик оставляет и следующий
/// «Старт» берёт его же, снятие возвращает прежний.
/// </summary>
public class WindowCallRunnerTests : IDisposable
{
    [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private delegate void Receiver(uint a, uint dataPointer);

    private static uint _a, _dataValue;
    private static int _thread;

    private static void Receive(uint a, uint dataPointer)
    {
        _a = a;
        _dataValue = (uint)Marshal.ReadInt32(new IntPtr(unchecked((int)dataPointer)));
        _thread = GetCurrentThreadId();
    }

    [UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    private const uint TestMessage = 0x0400 + 77; // WM_USER + 77

    private readonly IntPtr _window = CreateWindowEx(0, "STATIC", "", 0, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
    private readonly GameProcess _self = GameProcess.Open(Process.GetCurrentProcess().Id, GameProcessRights.Execute);
    private readonly WindowProc _gameProc;
    private readonly int _gameProcAddress;
    private readonly WindowProc _foreignProc = DefWindowProc;
    private uint _received;

    public WindowCallRunnerTests()
    {
        // Как у игры: обработчик окна — обычная функция (у системного STATIC Windows отдаёт его по-разному)
        _gameProc = (window, message, wParam, lParam) =>
        {
            if (message == TestMessage)
                _received = unchecked((uint)wParam.ToInt32());
            return DefWindowProc(window, message, wParam, lParam);
        };
        _gameProcAddress = Marshal.GetFunctionPointerForDelegate(_gameProc).ToInt32();
        SetWindowLong(_window, -4, _gameProcAddress);
    }

    public void Dispose()
    {
        DestroyWindow(_window);
        GC.KeepAlive(_gameProc);
        GC.KeepAlive(_foreignProc);
        _self.Dispose();
    }

    [Fact]
    public void CallRunsInWindowThread()
    {
        Receiver receiver = Receive;
        var function = unchecked((uint)Marshal.GetFunctionPointerForDelegate(receiver).ToInt32());
        using var runner = WindowCallRunner.Attach(_self, _window, out var problem);
        Assert.True(runner is not null, problem);

        var result = runner!.Run(BitConverter.GetBytes(0xC0FFEEu),
            data => StubBuilder.Call(function, BotCH.Core.Profiles.CallingConvention.Cdecl, 0, [111, data]));

        Assert.True(result.IsDone, result.Details);
        Assert.Equal((111u, 0xC0FFEEu), (_a, _dataValue));
        Assert.Equal(GetCurrentThreadId(), _thread); // поток окна, а не отдельный
        GC.KeepAlive(receiver);
    }

    [Fact]
    public void OtherMessagesReachOriginal()
    {
        using var runner = WindowCallRunner.Attach(_self, _window, out var problem);
        Assert.True(runner is not null, problem);
        Assert.NotEqual(_gameProcAddress, GetWindowLong(_window, -4));

        // Чужое сообщение доходит до «игры» через наш обработчик
        SendMessage(_window, TestMessage, new IntPtr(42), IntPtr.Zero);

        Assert.Equal(42u, _received);
    }

    [Fact]
    public void StopLeavesHandlerAndNextStartReusesIt()
    {
        var first = WindowCallRunner.Attach(_self, _window, out var problem);
        Assert.True(first is not null, problem);
        Assert.False(first!.Reused);
        var installed = GetWindowLong(_window, -4);

        first.Dispose();
        Assert.Equal(installed, GetWindowLong(_window, -4));
        Assert.Equal(RemoteRunStatus.Failed, first.Run(null, _ => [0x31, 0xC0, 0xC2, 0x04, 0x00]).Status);

        // Следующий «Старт» — тот же обработчик, новой страницы в игре нет
        using var second = WindowCallRunner.Attach(_self, _window, out problem);
        Assert.True(second is not null, problem);
        Assert.True(second!.Reused);
        Assert.Equal(installed, GetWindowLong(_window, -4));
        Assert.True(second.Run(null, _ => [0x31, 0xC0, 0xC2, 0x04, 0x00]).IsDone);
    }

    [Fact]
    public void RemoveBringsOriginalBack()
    {
        WindowCallRunner.Attach(_self, _window, out _)!.Dispose();

        Assert.True(WindowCallRunner.Remove(_self, _window, out var details), details);

        Assert.Equal(_gameProcAddress, GetWindowLong(_window, -4));
        Assert.False(WindowCallRunner.Remove(_self, _window, out _)); // снимать больше нечего
        SendMessage(_window, TestMessage, new IntPtr(7), IntPtr.Zero);
        Assert.Equal(7u, _received);
    }

    [Fact]
    public void ForeignHandlerOnTopIsNeitherRemovedNorReused()
    {
        // Кто-то поставил свой обработчик поверх нашего: снимать наш нельзя (снимем и его), брать — тоже (до нас не дойдёт)
        WindowCallRunner.Attach(_self, _window, out _)!.Dispose();
        var foreign = Marshal.GetFunctionPointerForDelegate(_foreignProc).ToInt32();
        SetWindowLong(_window, -4, foreign);

        Assert.False(WindowCallRunner.Remove(_self, _window, out _));
        Assert.Equal(foreign, GetWindowLong(_window, -4));

        using var runner = WindowCallRunner.Attach(_self, _window, out var problem);
        Assert.True(runner is not null, problem);
        Assert.False(runner!.Reused);
        Assert.True(runner.Run(null, _ => [0x31, 0xC0, 0xC2, 0x04, 0x00]).IsDone);
    }

    [Fact]
    public void HandlerReplacedOnTopReportsWindowLost()
    {
        // Кто-то поставил свой обработчик поверх нашего: сообщение до нас не доходит — «окно потеряно», а не «выполнено»
        using var runner = WindowCallRunner.Attach(_self, _window, out var problem);
        Assert.True(runner is not null, problem);
        SetWindowLong(_window, -4, _gameProcAddress);

        var result = runner!.Run(null, _ => [0x31, 0xC0, 0xC2, 0x04, 0x00]);

        Assert.Equal(RemoteRunStatus.WindowLost, result.Status);
    }

    [Fact]
    public void DestroyedWindowReportsWindowLostAtOnce()
    {
        // Игра пересоздала окно: старого нет — сразу «окно потеряно», без 5 с ожидания и без страницы в игре
        using var runner = WindowCallRunner.Attach(_self, _window, out var problem);
        Assert.True(runner is not null, problem);
        DestroyWindow(_window);

        var watch = Stopwatch.StartNew();
        var result = runner!.Run(null, _ => [0x31, 0xC0, 0xC2, 0x04, 0x00]);

        Assert.Equal(RemoteRunStatus.WindowLost, result.Status);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void NoWindowNoRunner()
    {
        Assert.Null(WindowCallRunner.Attach(_self, IntPtr.Zero, out var problem));
        Assert.Contains("окно", problem);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern int GetCurrentThreadId();
}

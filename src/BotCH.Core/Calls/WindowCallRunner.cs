using System;
using System.Runtime.InteropServices;
using BotCH.Core.Memory;

namespace BotCH.Core.Calls;

/// <summary>
/// Вызовы в главном потоке игры. Поток бота (CreateRemoteThread) работал параллельно игре — на стыке «работ» персонажа
/// (бег закончился → новый скилл) клиент 1.4.6 падал с повреждением кучи. Здесь окну игры ставится свой обработчик
/// (<see cref="WindowStubs.WindowProc"/>): на наше сообщение он выполняет заглушку вызова, остальное отдаёт прежнему.
/// Игра разбирает сообщения между кадрами — вызов идёт в безопасный момент, в её же потоке. Код игры не меняется.
/// </summary>
public sealed class WindowCallRunner : IRemoteRunner, IDisposable
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);
    private const int OriginalSlotOffset = 0x100;

    private readonly GameProcess _game;
    private readonly IntPtr _window;
    private readonly uint _message;
    private readonly uint _windowProc;
    private readonly uint _originalSlot;
    private readonly uint _getWindowLong;
    private readonly uint _setWindowLong;
    private bool _disposed;

    private WindowCallRunner(GameProcess game, IntPtr window, uint message, uint windowProc, uint getWindowLong, uint setWindowLong)
    {
        _game = game;
        _window = window;
        _message = message;
        _windowProc = windowProc;
        _originalSlot = windowProc + OriginalSlotOffset;
        _getWindowLong = getWindowLong;
        _setWindowLong = setWindowLong;
    }

    /// <summary>Поставить обработчик окну игры. null — не вышло, причина в <paramref name="problem"/> (вызывать по-старому, потоком).</summary>
    public static WindowCallRunner? Install(GameProcess game, IntPtr window, out string problem)
    {
        if (window == IntPtr.Zero || !IsWindow(window))
        {
            problem = "окно игры не найдено";
            return null;
        }

        // Адреса функций user32 берём у себя: системные библиотеки грузятся по одному адресу во всех процессах — проверяем
        var user32 = GetModuleHandle("user32.dll");
        var ours = unchecked((uint)user32.ToInt32());
        if (game.ModuleBase("user32.dll") is not { } theirs || theirs != ours)
        {
            problem = "user32.dll у игры по другому адресу";
            return null;
        }

        var unicode = IsWindowUnicode(window);
        uint Function(string name) => unchecked((uint)GetProcAddress(user32, name + (unicode ? "W" : "A")).ToInt32());
        var callWindowProc = Function("CallWindowProc");
        var defWindowProc = Function("DefWindowProc");
        var getWindowLong = Function("GetWindowLong");
        var setWindowLong = Function("SetWindowLong");
        var message = RegisterWindowMessage("BotCH.Call");
        if (callWindowProc == 0 || defWindowProc == 0 || getWindowLong == 0 || setWindowLong == 0 || message == 0)
        {
            problem = "нет функций user32";
            return null;
        }

        // Страница: обработчик в начале, слот «прежний обработчик» на 0x100 (пока 0 — обработчик отдаёт DefWindowProc)
        if (game.AllocateResident(new byte[OriginalSlotOffset + 4]) is not { } windowProc)
        {
            problem = "не удалось выделить память под обработчик";
            return null;
        }

        var code = WindowStubs.WindowProc(message, windowProc + OriginalSlotOffset, callWindowProc, defWindowProc);
        if (!game.TryWrite(windowProc, code))
        {
            problem = "не удалось записать обработчик";
            return null;
        }

        var window32 = unchecked((uint)window.ToInt32());
        var install = game.Run(null, _ => WindowStubs.Install(setWindowLong, window32, windowProc, windowProc + OriginalSlotOffset));
        if (!install.IsDone || !game.TryReadUInt32(windowProc + OriginalSlotOffset, out var original) || original == 0)
        {
            problem = $"обработчик не поставился{(install.IsDone ? "" : ": " + install.Details)}";
            return null;
        }

        problem = "";
        return new WindowCallRunner(game, window, message, windowProc, getWindowLong, setWindowLong);
    }

    public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub)
    {
        if (_disposed)
            return new RemoteRunResult(RemoteRunStatus.Failed, "обработчик окна уже снят");
        if (_game.PrepareCall(data, buildStub, out var page) is { } failed)
            return failed;

        var sent = SendMessageTimeout(_window, _message, new IntPtr(unchecked((int)page)), IntPtr.Zero, SmtoAbortIfHung,
            (uint)CallTimeout.TotalMilliseconds, out var result);
        if (sent == IntPtr.Zero)
        {
            // Сообщение могло остаться в очереди и выполниться позже — память не освобождаем
            return new RemoteRunResult(RemoteRunStatus.Timeout, $"игра не обработала вызов за {CallTimeout.TotalSeconds:0} с");
        }

        _game.FreePage(page);
        return unchecked((uint)result.ToInt32()) == WindowStubs.Handled
            ? new RemoteRunResult(RemoteRunStatus.Done)
            : new RemoteRunResult(RemoteRunStatus.Failed, "обработчик окна снят — вызов не выполнен");
    }

    /// <summary>Вернуть окну прежний обработчик (если поверх никто не поставил свой). Страница обработчика остаётся.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_game.HasExited || !IsWindow(_window))
            return;

        var window32 = unchecked((uint)_window.ToInt32());
        _game.Run(null, _ => WindowStubs.Restore(_getWindowLong, _setWindowLong, window32, _windowProc, _originalSlot));
    }

    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout,
        out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowUnicode(IntPtr window);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);
}

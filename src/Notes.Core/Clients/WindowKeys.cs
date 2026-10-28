using System;
using System.Runtime.InteropServices;
using System.Threading;
using Notes.Core.Actions;
using Notes.Core.Calls;

namespace Notes.Core.Clients;

/// <summary>
/// Нажатия клавиш сообщениями в главное окно клиента (PostMessage WM_KEYDOWN/WM_KEYUP) — не SendInput: тот жмёт в активное
/// окно, а там может быть соседний клиент, за которым играют. Ctrl/Alt/Shift — такими же сообщениями до и после клавиши;
/// смотрит ли игра на них, а не на настоящую клавиатуру, видно только в игре.
/// <para>
/// Окно игры в фоне — игра нажатие получает, но выкидывает: разбор клавиш раз в кадр начинается с её флага «приложение
/// активно», а флаг она ставит по WM_ACTIVATEAPP. Поэтому любое нажатие идёт между «ты активна» и «ты снова в фоне»
/// (WM_ACTIVATEAPP 1 и 0): Windows окно не активирует и фокус не трогает, меняется только флаг внутри игры. PW Classic:
/// флаг пишет оконная процедура FUN_007627a0 (ветка 0x1C), читает разбор очереди клавиш FUN_007bb3d0. Найти на другом
/// клиенте: в оконной процедуре ветка WM_ACTIVATEAPP → флаг из неё → функция, которая проверяет его перед WM_KEYDOWN.
/// </para>
/// </summary>
public sealed class WindowKeys : IGameKeys
{
    // Между «нажал» и «отпустил»: игра, которая смотрит, зажата ли клавиша, раз в кадр, иначе нажатие пропустит
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(50);

    // После отпускания, до «ты в фоне»: очередь клавиш игра разбирает раз в кадр (в фоне кадр ~15–30 мс), флаг «активна»
    // должен дожить до него. Приди «в фоне» одной пачкой с клавишей — нажатие пропало бы
    private static readonly TimeSpan AfterRelease = TimeSpan.FromMilliseconds(50);

    private const uint WmActivateApp = 0x001C, WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

    private readonly int _pid;
    private readonly IKeyWindow _system;
    private IntPtr _window;

    public WindowKeys(int pid) : this(pid, Win32KeyWindow.Instance)
    {
    }

    internal WindowKeys(int pid, IKeyWindow system)
    {
        _pid = pid;
        _system = system;
    }

    /// <summary>Главное окно клиента, куда уходят нажатия; нет — <see cref="IntPtr.Zero"/>.</summary>
    public IntPtr Window
    {
        get
        {
            if (_window == IntPtr.Zero || !_system.Exists(_window))
                _window = _system.Find(_pid);
            return _window;
        }
    }

    /// <summary>Клиент сейчас активный: активное окно — его.</summary>
    public bool IsActive => Window != IntPtr.Zero && _system.Foreground().Process == _pid;

    public CallResult Press(KeyStroke key)
    {
        if (key.Forbidden is { } why)
            return CallResult.Refused(why);

        var window = Window;
        if (window == IntPtr.Zero)
            return CallResult.Refused("окно игры не найдено");

        if (!_system.Post(window, WmActivateApp, 1, 0))
            return CallResult.Refused($"окно игры не приняло нажатие (ошибка {Marshal.GetLastWin32Error()})");

        // Alt без Ctrl — «системные» сообщения, как шлёт сама Windows: иначе игра Alt+клавишу не узнает
        var sys = key.Alt && !key.Ctrl;
        var pressed = (!key.Ctrl || Post(window, WmKeyDown, VkControl, false, up: false))
                      && (!key.Shift || Post(window, WmKeyDown, VkShift, false, up: false))
                      && (!key.Alt || Post(window, WmSysKeyDown, VkMenu, true, up: false))
                      && Post(window, sys ? WmSysKeyDown : WmKeyDown, key.Key, key.Alt, up: false);
        var error = pressed ? 0 : Marshal.GetLastWin32Error();
        if (pressed)
            _system.Wait(Hold);

        // Отпустить всё, даже если нажатие ушло не целиком: зажатый Ctrl в игре хуже лишнего сообщения. Alt — обычным
        // WM_KEYUP: «системное» отпускание Alt окно может принять за вход в своё меню и встать в нём
        var released = Post(window, sys ? WmSysKeyUp : WmKeyUp, key.Key, key.Alt, up: true);
        if (key.Alt)
            released &= Post(window, WmKeyUp, VkMenu, false, up: true);
        if (key.Shift)
            released &= Post(window, WmKeyUp, VkShift, false, up: true);
        if (key.Ctrl)
            released &= Post(window, WmKeyUp, VkControl, false, up: true);
        var releaseError = released ? 0 : Marshal.GetLastWin32Error();

        // «В фоне» — даже если клавиша не ушла: игра, что в фоне считает себя активной, хуже лишнего сообщения
        if (pressed)
            _system.Wait(AfterRelease);
        var back = Deactivate(window);

        if (!pressed)
            return CallResult.Refused($"окно игры не приняло нажатие (ошибка {error})");
        if (!released)
            return CallResult.Refused($"окно игры не приняло отпускание (ошибка {releaseError})");
        return back ? CallResult.Done : CallResult.Refused($"окно игры не приняло возврат в фон (ошибка {Marshal.GetLastWin32Error()})");
    }

    /// <summary>
    /// «Ты снова в фоне». Поток активного окна — нынешний, а не до нажатия: игрок мог переключить окно. Игра сама смотрит,
    /// чьё окно активно у этого потока: её собственное (игрок в ней) — флаг не сбрасывает.
    /// </summary>
    private bool Deactivate(IntPtr window)
    {
        var back = _system.Post(window, WmActivateApp, 0, _system.Foreground().Thread);
        // Игрок щёлкнул в игру, пока шло нажатие: Windows уже сказала ей «активна», а наше «в фоне» пришло следом и сбило
        // бы флаг — клавиши игрока пропадали бы, пока он не переключит окно
        if (_system.Foreground().Process == _pid)
            back &= _system.Post(window, WmActivateApp, 1, 0);
        return back;
    }

    private bool Post(IntPtr window, uint message, int key, bool alt, bool up)
        => _system.Post(window, message, key, LParam(key, MapVirtualKey((uint)key, MapVkToVsc), alt, up));

    /// <summary>
    /// Что Windows кладёт в lParam клавиатурного сообщения: повтор 1, скан-код, «расширенная» (стрелки, Insert…Delete, Num/),
    /// бит 29 — зажат Alt, 30 и 31 — у отпускания.
    /// </summary>
    internal static int LParam(int key, uint scanCode, bool alt, bool up)
    {
        var value = 1 | (int)((scanCode & 0xFF) << 16);
        if (Extended(key))
            value |= 1 << 24;
        if (alt)
            value |= 1 << 29;
        if (up)
            value |= unchecked((int)0xC0000000);
        return value;
    }

    private static bool Extended(int key) => key is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x6F or 0x90;

    private const uint MapVkToVsc = 0;

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}

/// <summary>Что <see cref="WindowKeys"/> нужно от Windows: окно клиента, сообщения в него, кто активен, пауза. В тестах — запись.</summary>
internal interface IKeyWindow
{
    /// <summary>Главное окно клиента; нет — <see cref="IntPtr.Zero"/>.</summary>
    IntPtr Find(int pid);

    bool Exists(IntPtr window);

    /// <summary>PostMessage; false — код ошибки в <see cref="Marshal.GetLastWin32Error"/>.</summary>
    bool Post(IntPtr window, uint message, int wParam, int lParam);

    /// <summary>Поток и процесс активного окна; активного нет — нули.</summary>
    (int Thread, int Process) Foreground();

    void Wait(TimeSpan time);
}

/// <summary>Настоящие окна Windows.</summary>
internal sealed class Win32KeyWindow : IKeyWindow
{
    public static readonly Win32KeyWindow Instance = new();

    public IntPtr Find(int pid) => NativeWindows.FindMainWindow(pid, includeHidden: true);

    public bool Exists(IntPtr window) => IsWindow(window);

    public bool Post(IntPtr window, uint message, int wParam, int lParam)
        => PostMessage(window, message, (IntPtr)wParam, (IntPtr)lParam);

    public (int Thread, int Process) Foreground()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
            return (0, 0);
        var thread = GetWindowThreadProcessId(window, out var process);
        return (unchecked((int)thread), process);
    }

    public void Wait(TimeSpan time) => Thread.Sleep(time);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
}

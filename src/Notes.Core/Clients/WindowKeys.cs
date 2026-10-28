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
/// </summary>
public sealed class WindowKeys(int pid) : IGameKeys
{
    // Между «нажал» и «отпустил»: игра, которая смотрит, зажата ли клавиша, раз в кадр, иначе нажатие пропустит
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(50);

    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;

    private IntPtr _window;

    /// <summary>Главное окно клиента, куда уходят нажатия; нет — <see cref="IntPtr.Zero"/>.</summary>
    public IntPtr Window
    {
        get
        {
            if (_window == IntPtr.Zero || !IsWindow(_window))
                _window = NativeWindows.FindMainWindow(pid, includeHidden: true);
            return _window;
        }
    }

    /// <summary>Окно клиента сейчас активное (в нём фокус).</summary>
    public bool IsActive => Window is var w && w != IntPtr.Zero && GetForegroundWindow() == w;

    public CallResult Press(KeyStroke key)
    {
        if (key.Forbidden is { } why)
            return CallResult.Refused(why);

        var window = Window;
        if (window == IntPtr.Zero)
            return CallResult.Refused("окно игры не найдено");

        // Alt без Ctrl — «системные» сообщения, как шлёт сама Windows: иначе игра Alt+клавишу не узнает
        var sys = key.Alt && !key.Ctrl;
        var pressed = (!key.Ctrl || Post(window, WmKeyDown, VkControl, false, up: false))
                      && (!key.Shift || Post(window, WmKeyDown, VkShift, false, up: false))
                      && (!key.Alt || Post(window, WmSysKeyDown, VkMenu, true, up: false))
                      && Post(window, sys ? WmSysKeyDown : WmKeyDown, key.Key, key.Alt, up: false);
        var error = pressed ? 0 : Marshal.GetLastWin32Error();
        if (pressed)
            Thread.Sleep(Hold);

        // Отпустить всё, даже если нажатие ушло не целиком: зажатый Ctrl в игре хуже лишнего сообщения. Alt — обычным
        // WM_KEYUP: «системное» отпускание Alt окно может принять за вход в своё меню и встать в нём
        var released = Post(window, sys ? WmSysKeyUp : WmKeyUp, key.Key, key.Alt, up: true);
        if (key.Alt)
            released &= Post(window, WmKeyUp, VkMenu, false, up: true);
        if (key.Shift)
            released &= Post(window, WmKeyUp, VkShift, false, up: true);
        if (key.Ctrl)
            released &= Post(window, WmKeyUp, VkControl, false, up: true);

        if (!pressed)
            return CallResult.Refused($"окно игры не приняло нажатие (ошибка {error})");
        return released ? CallResult.Done : CallResult.Refused($"окно игры не приняло отпускание (ошибка {Marshal.GetLastWin32Error()})");
    }

    private static bool Post(IntPtr window, uint message, int key, bool alt, bool up)
        => PostMessage(window, message, (IntPtr)key, (IntPtr)LParam(key, MapVirtualKey((uint)key, MapVkToVsc), alt, up));

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
}

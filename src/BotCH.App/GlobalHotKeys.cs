using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BotCH.App;

/// <summary>
/// Горячие клавиши, которые работают, даже когда в фокусе игра (RegisterHotKey).
/// Ctrl+Alt+Num1 — старт, Ctrl+Alt+Num0 — стоп, как в старом боте.
/// </summary>
internal sealed class GlobalHotKeys : IDisposable
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint NoRepeat = 0x4000;
    public const uint NumPad0 = 0x60;
    public const uint NumPad1 = 0x61;

    private const int WmHotKey = 0x0312;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 1;

    public GlobalHotKeys(IntPtr window)
    {
        _source = HwndSource.FromHwnd(window) ?? throw new InvalidOperationException("Окно ещё не создано");
        _source.AddHook(Hook);
    }

    /// <summary>false — сочетание уже занято другой программой.</summary>
    public bool Register(uint modifiers, uint key, Action action)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers | NoRepeat, key))
            return false;

        _actions[id] = action;
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys)
            UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
        _source.RemoveHook(Hook);
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotKey && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}

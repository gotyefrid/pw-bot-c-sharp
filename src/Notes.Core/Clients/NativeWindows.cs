using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Notes.Core.Clients;

/// <summary>
/// Окна игры через WinAPI: найти клиентов и главное окно процесса, прочитать и сменить заголовок и значок. В память игры не лезет.
/// </summary>
public static class NativeWindows
{
    // Класс окна игры — «ElementClient Window»: его заводит движок игры, и он один у всех клиентов, как бы ни назывался exe
    // (проверено в ElementClient.exe и ElementClient_test.exe Comeback, Client.exe и ElementClient.exe PW Classic)
    private const string ClientClassPrefix = "ElementClient";

    /// <summary>PID всех клиентов игры по возрастанию: процессы, у которых есть окно игры (видимое или скрытое в трее).</summary>
    public static List<int> FindClientProcesses()
    {
        var pids = new HashSet<int>();
        EnumWindows((window, _) =>
        {
            if (IsClientWindowClass(ClassName(window)))
            {
                GetWindowThreadProcessId(window, out var pid);
                pids.Add(pid);
            }
            return true;
        }, IntPtr.Zero);
        return pids.OrderBy(pid => pid).ToList();
    }

    public static bool IsClientWindowClass(string className) => className.StartsWith(ClientClassPrefix, StringComparison.Ordinal);

    /// <summary>
    /// Видимое окно верхнего уровня без владельца, принадлежащее процессу. Надёжнее Process.MainWindowHandle,
    /// который бывает 0 у свёрнутого окна или пока клиент грузится.
    /// </summary>
    public static IntPtr FindMainWindow(int pid) => FindMainWindow(pid, includeHidden: false);

    /// <param name="includeHidden">
    /// Видимого нет (клиент свёрнут в трей) — взять скрытое окно клиента (класс «ElementClient…»); у игры есть и другие
    /// скрытые окна без владельца, их не берём.
    /// </param>
    public static IntPtr FindMainWindow(int pid, bool includeHidden)
    {
        IntPtr visible = IntPtr.Zero, hidden = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var owner);
            if (owner != pid || GetWindow(window, GwOwner) != IntPtr.Zero)
                return true;

            if (IsWindowVisible(window))
            {
                visible = window;
                return false;
            }

            if (hidden == IntPtr.Zero && IsClientWindowClass(ClassName(window)))
                hidden = window;
            return true;
        }, IntPtr.Zero);
        return visible != IntPtr.Zero || !includeHidden ? visible : hidden;
    }

    private static string ClassName(IntPtr window)
    {
        var name = new StringBuilder(256);
        return GetClassName(window, name, name.Capacity) > 0 ? name.ToString() : "";
    }

    public static string GetTitle(IntPtr window)
    {
        var length = GetWindowTextLength(window);
        var text = new StringBuilder(length + 1);
        GetWindowText(window, text, text.Capacity);
        return text.ToString();
    }

    /// <summary>Меняет заголовок, только если он другой. true — заголовок теперь такой, как нужно.</summary>
    public static bool SetTitle(IntPtr window, string title)
        => window != IntPtr.Zero && (GetTitle(window) == title || SetWindowText(window, title));

    /// <summary>
    /// Родной значок окна — значок класса окна (его заводит игра при создании окна; <see cref="SetIcon"/> его не трогает, поэтому
    /// после перезапуска бота он всё тот же, без наших букв). Нет — значок из exe (освободить <see cref="DestroyIcon"/>).
    /// </summary>
    /// <returns>Значок и нужно ли его освобождать; нет ни того, ни другого — <see cref="IntPtr.Zero"/>.</returns>
    public static (IntPtr Icon, bool Owned) OriginalIcon(IntPtr window, string? exePath)
    {
        var icon = window == IntPtr.Zero ? IntPtr.Zero : (IntPtr)GetClassLong(window, GclHIcon);
        if (icon != IntPtr.Zero || exePath is null)
            return (icon, false);
        var big = new IntPtr[1];
        return ExtractIconEx(exePath, 0, big, null, 1) > 0 ? (big[0], true) : (IntPtr.Zero, false);
    }

    /// <summary>
    /// Значок окна (большой и маленький — один: Windows уменьшит). <see cref="IntPtr.Zero"/> — снять свой, окно снова со значком
    /// класса. Окно игры занято (загрузка) — не ждём дольше секунды.
    /// </summary>
    public static bool SetIcon(IntPtr window, IntPtr icon)
        => window != IntPtr.Zero
            && SendMessageTimeout(window, WmSetIcon, (IntPtr)IconBig, icon, SmtoAbortIfHung, 1000, out _) != IntPtr.Zero
            && SendMessageTimeout(window, WmSetIcon, (IntPtr)IconSmall, icon, SmtoAbortIfHung, 1000, out _) != IntPtr.Zero;

    /// <summary>Значок из картинки PNG (квадратной). Освободить <see cref="DestroyIcon"/>.</summary>
    public static IntPtr IconFromPng(byte[] png, int size)
        => CreateIconFromResourceEx(png, (uint)png.Length, true, 0x00030000, size, size, 0);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr icon);

    private const int GclHIcon = -14;
    private const uint WmSetIcon = 0x0080;
    private const int IconSmall = 0;
    private const int IconBig = 1;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll")]
    private static extern uint GetClassLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout,
        out IntPtr result);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, bool icon, uint version, int width, int height, uint flags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    private const uint GwOwner = 4;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder name, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr window, string text);
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BotCH.Core.Clients;

/// <summary>Окна игры через WinAPI: найти клиентов и главное окно процесса, прочитать и сменить заголовок. В память игры не лезет.</summary>
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

using System;
using System.Collections.Generic;

namespace BotCH.Core.Calls;

/// <summary>
/// Машинный код для вызовов в главном потоке игры через обработчик её окна (см. <see cref="WindowCallRunner"/>).
/// Код игры не меняется: подменяется только указатель на обработчик окна (SetWindowLong), как делает любой subclassing.
/// </summary>
public static class WindowStubs
{
    /// <summary>Значение, которое наш обработчик возвращает на своё сообщение: им бот понимает, что вызов выполнен им.</summary>
    public const uint Handled = 0xB07C_A11D;

    private const byte GwlWndProc = 0xFC; // -4

    /// <summary>
    /// Обработчик окна (stdcall: hwnd, msg, wParam, lParam). Своё сообщение: wParam — адрес заглушки вызова
    /// (как у потока: stdcall(void*)), выполнить её и вернуть <see cref="Handled"/>. Остальные — прежнему обработчику
    /// (адрес в <paramref name="originalSlot"/>) через CallWindowProc; пока он не записан (0) — DefWindowProc.
    /// </summary>
    public static byte[] WindowProc(uint message, uint originalSlot, uint callWindowProc, uint defWindowProc)
    {
        var code = new List<byte>();
        code.AddRange([0x81, 0x7C, 0x24, 0x08]);            // cmp dword [esp+8], message
        code.AddRange(BitConverter.GetBytes(message));
        var jnePass = code.Count;
        code.AddRange([0x75, 0x00]);                        // jne pass
        code.AddRange([0x8B, 0x44, 0x24, 0x0C]);            // mov eax, [esp+0Ch] (wParam)
        code.AddRange([0x6A, 0x00]);                        // push 0
        code.AddRange([0xFF, 0xD0]);                        // call eax
        code.Add(0xB8);                                     // mov eax, Handled
        code.AddRange(BitConverter.GetBytes(Handled));
        code.AddRange([0xC2, 0x10, 0x00]);                  // ret 10h

        code[jnePass + 1] = (byte)(code.Count - (jnePass + 2));
        code.Add(0xA1);                                     // pass: mov eax, [originalSlot]
        code.AddRange(BitConverter.GetBytes(originalSlot));
        code.AddRange([0x85, 0xC0]);                        // test eax, eax
        var jzDefault = code.Count;
        code.AddRange([0x74, 0x00]);                        // jz default
        for (var i = 0; i < 4; i++)
            code.AddRange([0xFF, 0x74, 0x24, 0x10]);        // push [esp+10h] ×4: lParam, wParam, msg, hwnd
        code.Add(0x50);                                     // push eax (прежний обработчик)
        CallAbsolute(code, callWindowProc);
        code.AddRange([0xC2, 0x10, 0x00]);                  // ret 10h

        code[jzDefault + 1] = (byte)(code.Count - (jzDefault + 2));
        code.Add(0xB8);                                     // default: mov eax, DefWindowProc; jmp eax (аргументы те же)
        code.AddRange(BitConverter.GetBytes(defWindowProc));
        code.AddRange([0xFF, 0xE0]);
        return code.ToArray();
    }

    /// <summary>Поток: <c>[originalSlot] = SetWindowLong(window, GWL_WNDPROC, newProc)</c>.</summary>
    public static byte[] Install(uint setWindowLong, uint window, uint newProc, uint originalSlot)
    {
        var code = new List<byte>();
        Push(code, newProc);
        code.AddRange([0x6A, GwlWndProc]);
        Push(code, window);
        CallAbsolute(code, setWindowLong);
        code.Add(0xA3);                                     // mov [originalSlot], eax
        code.AddRange(BitConverter.GetBytes(originalSlot));
        Return(code);
        return code.ToArray();
    }

    /// <summary>
    /// Поток: какой обработчик сейчас у окна — <c>return GetWindowLong(window, GWL_WNDPROC)</c> (адрес — в коде выхода потока).
    /// Изнутри игры: из своего процесса для чужого окна Windows может отдать не адрес, а служебное значение.
    /// </summary>
    public static byte[] ReadWindowProc(uint getWindowLong, uint window)
    {
        var code = new List<byte>();
        code.AddRange([0x6A, GwlWndProc]);
        Push(code, window);
        CallAbsolute(code, getWindowLong);
        code.AddRange([0xC2, 0x04, 0x00]);                  // ret 4 (eax — обработчик)
        return code.ToArray();
    }

    /// <summary>
    /// Поток: вернуть прежний обработчик, только если сейчас стоит наш (кто-то мог подменить поверх — его не трогаем).
    /// <c>if (GetWindowLong(window, GWL_WNDPROC) == ourProc) SetWindowLong(window, GWL_WNDPROC, [originalSlot])</c>.
    /// </summary>
    public static byte[] Restore(uint getWindowLong, uint setWindowLong, uint window, uint ourProc, uint originalSlot)
    {
        var code = new List<byte>();
        code.AddRange([0x6A, GwlWndProc]);
        Push(code, window);
        CallAbsolute(code, getWindowLong);
        code.Add(0x3D);                                     // cmp eax, ourProc
        code.AddRange(BitConverter.GetBytes(ourProc));
        var jne = code.Count;
        code.AddRange([0x75, 0x00]);                        // jne end
        code.AddRange([0xFF, 0x35]);                        // push [originalSlot]
        code.AddRange(BitConverter.GetBytes(originalSlot));
        code.AddRange([0x6A, GwlWndProc]);
        Push(code, window);
        CallAbsolute(code, setWindowLong);
        code[jne + 1] = (byte)(code.Count - (jne + 2));
        Return(code);                                       // end:
        return code.ToArray();
    }

    private static void Push(List<byte> code, uint value)
    {
        code.Add(0x68);
        code.AddRange(BitConverter.GetBytes(value));
    }

    // mov eax, func; call eax
    private static void CallAbsolute(List<byte> code, uint function)
    {
        code.Add(0xB8);
        code.AddRange(BitConverter.GetBytes(function));
        code.AddRange([0xFF, 0xD0]);
    }

    // xor eax, eax; ret 4
    private static void Return(List<byte> code) => code.AddRange([0x31, 0xC0, 0xC2, 0x04, 0x00]);
}

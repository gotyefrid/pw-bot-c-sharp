using System;
using System.Collections.Generic;
using BotCH.Core.Profiles;

namespace BotCH.Core.Calls;

/// <summary>
/// Машинный код маленькой заглушки, которую поток в игре выполняет, чтобы вызвать функцию клиента.
/// Байты — как в старом GameCall (проверено в игре). Заглушка — функция потока: stdcall(void*), возвращает 0.
/// </summary>
public static class StubBuilder
{
    /// <summary>
    /// <c>push argN … push arg1; [mov ecx, this]; mov eax, func; call eax; [add esp, 4*N]; xor eax, eax; ret 4</c>.
    /// cdecl — стек после вызова чистим сами; thiscall — this в ecx, стек чистит функция; stdcall — стек чистит функция.
    /// </summary>
    public static byte[] Call(uint function, CallingConvention convention, uint thisPointer, IReadOnlyList<uint> args)
    {
        if (convention == CallingConvention.Thiscall && thisPointer == 0)
            throw new ArgumentException("Для thiscall нужен объект (this)", nameof(thisPointer));
        if (args.Count > 31)
            throw new ArgumentException("Слишком много аргументов", nameof(args));

        var code = new List<byte>();
        for (var i = args.Count - 1; i >= 0; i--)
            Push(code, args[i]);

        if (convention == CallingConvention.Thiscall)
            MovEcx(code, thisPointer);

        CallAbsolute(code, function);

        if (convention == CallingConvention.Cdecl && args.Count > 0)
            code.AddRange([0x83, 0xC4, (byte)(4 * args.Count)]); // add esp, 4*N

        Return(code);
        return code.ToArray();
    }

    /// <summary>
    /// Идти в точку, как кликом по земле:
    /// <c>work = WorkMan->CreateWork(1); if (work) { work->SetDestination(type, &amp;point); WorkMan->StartWork(1, work, 1, 0); }</c>.
    /// </summary>
    public static byte[] MoveTo(uint workMan, uint create, uint setDestination, uint start, uint pointAddress, byte destinationType)
    {
        var code = new List<byte> { 0x56 };                 // push esi
        MovEcx(code, workMan);
        code.AddRange([0x6A, 0x01]);                        // push 1 (CECHPWorkMove)
        CallAbsolute(code, create);
        code.AddRange([0x85, 0xC0]);                        // test eax, eax
        var jz = code.Count;
        code.AddRange([0x74, 0x00]);                        // jz end — смещение ниже
        code.AddRange([0x8B, 0xF0]);                        // mov esi, eax
        Push(code, pointAddress);                           // push &point
        code.AddRange([0x6A, destinationType]);             // push type (0 — точка на земле)
        code.AddRange([0x8B, 0xCE]);                        // mov ecx, esi
        CallAbsolute(code, setDestination);
        code.AddRange([0x6A, 0x00, 0x6A, 0x01, 0x56, 0x6A, 0x01]); // push 0; push 1; push esi; push 1
        MovEcx(code, workMan);
        CallAbsolute(code, start);
        code[jz + 1] = (byte)(code.Count - (jz + 2));
        code.Add(0x5E);                                     // end: pop esi
        Return(code);
        return code.ToArray();
    }

    private static void Push(List<byte> code, uint value)
    {
        code.Add(0x68);
        code.AddRange(BitConverter.GetBytes(value));
    }

    private static void MovEcx(List<byte> code, uint value)
    {
        code.Add(0xB9);
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

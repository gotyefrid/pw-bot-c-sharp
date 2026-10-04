using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Profiles;

namespace BotCH.Core.Calls;

/// <summary>
/// Машинный код маленькой заглушки, которую поток в игре выполняет, чтобы вызвать функцию клиента.
/// Байты — как в старом GameCall (проверено в игре). Заглушка — функция потока: stdcall(void*), возвращает 0.
/// </summary>
public static class StubBuilder
{
    /// <summary>
    /// <c>push argN … push arg1; [mov ecx, this]; [mov ecx/edx, значение]; mov eax, func; call eax; [add esp, 4*N]; xor eax, eax; ret 4</c>.
    /// cdecl — стек после вызова чистим сами; thiscall — this в ecx, стек чистит функция; stdcall — стек чистит функция.
    /// ecx/edx — аргументы в регистрах (так собраны некоторые функции Comeback 1.4.6).
    /// </summary>
    public static byte[] Call(uint function, CallingConvention convention, uint thisPointer, IReadOnlyList<uint> args,
        uint? ecx = null, uint? edx = null)
    {
        if (convention == CallingConvention.Thiscall && thisPointer == 0)
            throw new ArgumentException("Для thiscall нужен объект (this)", nameof(thisPointer));
        if (convention == CallingConvention.Thiscall && ecx is not null)
            throw new ArgumentException("У thiscall в ecx уже лежит объект", nameof(ecx));
        if (args.Count > 31)
            throw new ArgumentException("Слишком много аргументов", nameof(args));

        var code = new List<byte>();
        for (var i = args.Count - 1; i >= 0; i--)
            Push(code, args[i]);

        if (convention == CallingConvention.Thiscall)
            MovEcx(code, thisPointer);
        if (ecx is { } ecxValue)
            MovEcx(code, ecxValue);
        if (edx is { } edxValue)
        {
            code.Add(0xBA);                                 // mov edx, значение
            code.AddRange(BitConverter.GetBytes(edxValue));
        }

        CallAbsolute(code, function);

        if (convention == CallingConvention.Cdecl && args.Count > 0)
            code.AddRange([0x83, 0xC4, (byte)(4 * args.Count)]); // add esp, 4*N

        Return(code);
        return code.ToArray();
    }

    /// <summary>
    /// Идти в точку, как кликом по земле:
    /// <c>work = WorkMan->CreateWork(1); if (work) { work->SetDestination(destinationArgs); WorkMan->StartWork(startArgs); }</c>.
    /// Аргументы — по порядку, null — сама работа. 1.3.6: SetDestination(0, &amp;point), StartWork(1, work, 1, 0);
    /// Comeback 1.4.6: SetDestination(5, &amp;point) — с автопутём, StartWork(1, work, 0).
    /// </summary>
    public static byte[] MoveTo(uint workMan, uint create, uint setDestination, IReadOnlyList<uint> destinationArgs, uint start,
        IReadOnlyList<uint?> startArgs)
    {
        var code = new List<byte> { 0x56 };                 // push esi
        MovEcx(code, workMan);
        code.AddRange([0x6A, 0x01]);                        // push 1 (CECHPWorkMove)
        CallAbsolute(code, create);
        code.AddRange([0x85, 0xC0]);                        // test eax, eax
        var jz = code.Count;
        code.AddRange([0x74, 0x00]);                        // jz end — смещение ниже
        code.AddRange([0x8B, 0xF0]);                        // mov esi, eax
        PushArgs(code, destinationArgs.Select(a => (uint?)a).ToArray()); // тип, &point
        code.AddRange([0x8B, 0xCE]);                        // mov ecx, esi
        CallAbsolute(code, setDestination);
        PushArgs(code, startArgs);
        MovEcx(code, workMan);
        CallAbsolute(code, start);
        code[jz + 1] = (byte)(code.Count - (jz + 2));
        code.Add(0x5E);                                     // end: pop esi
        Return(code);
        return code.ToArray();
    }

    // Справа налево; null — push esi (работа), маленькие числа — push imm8
    private static void PushArgs(List<byte> code, IReadOnlyList<uint?> args)
    {
        for (var i = args.Count - 1; i >= 0; i--)
        {
            if (args[i] is not { } value)
                code.Add(0x56);
            else if (value <= 0x7F)
                code.AddRange([0x6A, (byte)value]);
            else
                Push(code, value);
        }
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

using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Memory;

namespace Notes.Core.Profiles;

/// <summary>
/// Адрес глобальной переменной клиента — по коду, который с ней работает: сигнатура кода, а в ней на месте <c>at</c> — сам адрес
/// (например, «mov ecx, адрес»). Как база (<see cref="BasePointerSearch"/>): адрес в профиле не хранится — при пересборке exe
/// переменные переезжают, а код вокруг них обычно остаётся тем же.
/// </summary>
public static class CodeDataSearch
{
    /// <summary>Найти в загруженном модуле; не нашлось или нашлось несколько мест — <see cref="InvalidOperationException"/> с причиной.</summary>
    public static uint Find(IMemory memory, uint moduleBase, Signature signature, uint at)
    {
        var code = ModuleSections.Read(memory, moduleBase)
            .Where(s => s.IsCode && s.Size > 0)
            .Select(s => (s.Address, ModuleSections.ReadRegion(memory, s.Address, s.Size)));
        return Find(code, signature, at);
    }

    /// <param name="code">Код модуля по секциям: адрес начала и байты.</param>
    public static uint Find(IEnumerable<(uint Address, byte[] Bytes)> code, Signature signature, uint at)
    {
        if (at + 4 > signature.Length)
            throw new InvalidOperationException($"Адрес на +{at} выходит за сигнатуру длиной {signature.Length}");

        var values = new List<uint>();
        foreach (var (_, bytes) in code)
            values.AddRange(signature.FindAll(bytes, 2).Select(offset => BitConverter.ToUInt32(bytes, offset + (int)at)));

        return values.Count switch
        {
            0 => throw new InvalidOperationException($"Код «{signature}» не найден в клиенте"),
            1 => values[0],
            _ => throw new InvalidOperationException($"Код «{signature}» найден в нескольких местах — какое верное, неизвестно"),
        };
    }
}

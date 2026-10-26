using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Memory;

namespace BotCH.Core.Profiles;

/// <summary>Указатель на базу и сколько раз его читает код; рядом — вторая по чтениям переменная (для Probe).</summary>
public sealed record BasePointer(uint Address, int Reads, uint Second, int SecondReads);

/// <summary>
/// Где лежит указатель на базу ([указатель] = база, от неё game, персонаж, связь с сервером). В профиле его нет: при пересборке
/// exe он переезжает. База — глобальная переменная, которую код клиента читает чаще всех: у всех известных клиентов (Comeback
/// 1.3.6 и 1.4.6, PW Classic client.exe и elementclient.exe 2009 года) первая, в 2,5–3,5 раза чаще второй. Считаем чтения
/// «mov reg, [адрес]» по всему коду модуля. Не считаем проверку стека: «mov eax, [cookie]; xor eax, ebp» стоит в начале почти
/// каждой функции и читается чаще базы.
/// </summary>
public static class BasePointerSearch
{
    // Вторая переменная должна отставать хотя бы вдвое — иначе неясно, какая из них база
    private const int MinLead = 2;

    /// <summary>Найти в загруженном модуле; не нашлась — <see cref="InvalidOperationException"/> с причиной.</summary>
    public static BasePointer Find(IMemory memory, uint moduleBase)
    {
        var sections = ModuleSections.Read(memory, moduleBase);
        var data = sections.Where(s => s.IsWritable && !s.IsCode).ToList();
        var code = sections.Where(s => s.IsCode && s.Size > 0).Select(s => ModuleSections.ReadRegion(memory, s.Address, s.Size));
        return Rank(code, address => data.Any(s => address >= s.Address && address - s.Address < (uint)s.Size));
    }

    /// <param name="code">Код модуля по секциям.</param>
    /// <param name="isData">Адрес лежит в данных модуля (там, куда можно писать).</param>
    public static BasePointer Rank(IEnumerable<byte[]> code, Func<uint, bool> isData)
    {
        var reads = new Dictionary<uint, int>();
        foreach (var bytes in code)
        {
            for (var i = 0; i + 6 <= bytes.Length; i++)
            {
                uint address;
                // A1 — mov eax, [адрес]; проверка стека — за ним xor (33 C5, 33 C4, 31 45)
                if (bytes[i] == 0xA1 && bytes[i + 5] is not (0x33 or 0x31))
                    address = BitConverter.ToUInt32(bytes, i + 1);
                // 8B + ModRM 00 reg 101 — mov reg, [адрес]
                else if (bytes[i] == 0x8B && (bytes[i + 1] & 0xC7) == 0x05)
                    address = BitConverter.ToUInt32(bytes, i + 2);
                else
                    continue;

                if (isData(address))
                    reads[address] = reads.TryGetValue(address, out var n) ? n + 1 : 1;
            }
        }

        var top = reads.OrderByDescending(r => r.Value).Take(2).ToList();
        if (top.Count == 0)
            throw new InvalidOperationException("База не найдена: код не читает ни одной переменной — это не клиент игры?");

        var second = top.Count > 1 ? top[1] : default;
        if (second.Value * MinLead > top[0].Value)
            throw new InvalidOperationException($"База не найдена: переменные 0x{top[0].Key:X8} и 0x{second.Key:X8} читаются почти одинаково "
                                                + $"({top[0].Value} и {second.Value} раз)");

        return new BasePointer(top[0].Key, top[0].Value, second.Key, second.Value);
    }
}

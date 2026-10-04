using System;
using System.Text;

namespace BotCH.Core.Memory;

/// <summary>
/// Типизированное чтение поверх <see cref="IMemory"/>. Клиент 32-битный: указатели — uint, порядок байтов little-endian.
/// Методы Read* бросают <see cref="MemoryAccessException"/>, TryRead* возвращают false.
/// </summary>
public static class MemoryExtensions
{
    public static byte[] ReadBytes(this IMemory memory, uint address, int count)
    {
        var buffer = new byte[count];
        if (!memory.TryRead(address, buffer, count))
            throw new MemoryAccessException(address, $"Не удалось прочитать {count} байт");
        return buffer;
    }

    public static byte ReadByte(this IMemory memory, uint address) => memory.ReadBytes(address, 1)[0];

    public static ushort ReadUInt16(this IMemory memory, uint address) => BitConverter.ToUInt16(memory.ReadBytes(address, 2), 0);

    public static uint ReadUInt32(this IMemory memory, uint address) => BitConverter.ToUInt32(memory.ReadBytes(address, 4), 0);

    public static int ReadInt32(this IMemory memory, uint address) => BitConverter.ToInt32(memory.ReadBytes(address, 4), 0);

    public static float ReadFloat(this IMemory memory, uint address) => BitConverter.ToSingle(memory.ReadBytes(address, 4), 0);

    public static bool TryReadUInt32(this IMemory memory, uint address, out uint value)
    {
        var buffer = new byte[4];
        if (!memory.TryRead(address, buffer, 4))
        {
            value = 0;
            return false;
        }

        value = BitConverter.ToUInt32(buffer, 0);
        return true;
    }

    /// <summary>
    /// Строка UTF-16 (ники, названия в клиенте) до нулевого символа, не длиннее <paramref name="maxChars"/>.
    /// Если строка у конца доступной памяти — читает, сколько получится.
    /// </summary>
    public static string ReadUnicodeString(this IMemory memory, uint address, int maxChars = 64)
    {
        var bytes = memory.ReadBytesUpTo(address, maxChars * 2);
        var text = Encoding.Unicode.GetString(bytes, 0, bytes.Length & ~1);
        var end = text.IndexOf('\0');
        return end >= 0 ? text.Substring(0, end) : text;
    }

    /// <summary>
    /// Идёт по цепочке указателей и возвращает конечный адрес (сам он не читается):
    /// адрес = start; для каждого смещения: адрес = [адрес] + смещение.
    /// Пример: game = ReadUInt32(FollowPointers(module + 0x5B3EEC, 0x1C)) — это [[module+0x5B3EEC]+0x1C].
    /// Нулевой указатель по дороге — исключение (объекта ещё/уже нет).
    /// </summary>
    public static uint FollowPointers(this IMemory memory, uint start, params uint[] offsets)
    {
        var address = start;
        foreach (var offset in offsets)
        {
            var pointer = memory.ReadUInt32(address);
            if (pointer == 0)
                throw new MemoryAccessException(address, "Нулевой указатель в цепочке");
            address = pointer + offset;
        }

        return address;
    }

    /// <summary>Значение в конце цепочки: ReadUInt32(FollowPointers(start, offsets)).</summary>
    public static uint ReadPointerChain(this IMemory memory, uint start, params uint[] offsets)
        => memory.ReadUInt32(memory.FollowPointers(start, offsets));

    public static void WriteBytes(this IMemory memory, uint address, byte[] data)
    {
        if (!memory.TryWrite(address, data))
            throw new MemoryAccessException(address, $"Не удалось записать {data.Length} байт");
    }

    public static void WriteUInt32(this IMemory memory, uint address, uint value)
        => memory.WriteBytes(address, BitConverter.GetBytes(value));

    // Строки: сначала пробуем целиком, у границы доступной памяти — укорачиваем по страницам
    private static byte[] ReadBytesUpTo(this IMemory memory, uint address, int count)
    {
        var buffer = new byte[count];
        if (memory.TryRead(address, buffer, count))
            return buffer;

        const uint pageSize = 0x1000;
        var untilPageEnd = (int)(pageSize - address % pageSize);
        if (untilPageEnd < count && memory.TryRead(address, buffer, untilPageEnd))
        {
            Array.Resize(ref buffer, untilPageEnd);
            return buffer;
        }

        throw new MemoryAccessException(address, "Не удалось прочитать строку");
    }
}

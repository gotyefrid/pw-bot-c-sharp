using System;

namespace BotCH.Core.Memory;

/// <summary>
/// Кусок структуры, прочитанный из игры одним вызовом. Поля берутся уже из него —
/// так объект читается за один ReadProcessMemory вместо десятка, и все поля согласованы между собой.
/// </summary>
public readonly struct MemoryBlock(uint address, byte[] data)
{
    public uint Address { get; } = address;
    public int Size => data.Length;

    public static bool TryRead(IMemory memory, uint address, int size, out MemoryBlock block)
    {
        var data = new byte[size];
        block = new MemoryBlock(address, data);
        return address != 0 && memory.TryRead(address, data, size);
    }

    public static MemoryBlock Read(IMemory memory, uint address, int size)
        => TryRead(memory, address, size, out var block) ? block : throw new MemoryAccessException(address, $"Не удалось прочитать {size} байт");

    public uint UInt32(uint offset) => BitConverter.ToUInt32(data, Check(offset, 4));
    public int Int32(uint offset) => BitConverter.ToInt32(data, Check(offset, 4));
    public float Float(uint offset) => BitConverter.ToSingle(data, Check(offset, 4));
    public byte Byte(uint offset) => data[Check(offset, 1)];

    private int Check(uint offset, int size)
        => offset + size <= data.Length
            ? (int)offset
            : throw new ArgumentOutOfRangeException(nameof(offset), $"Поле +0x{offset:X} за пределами прочитанного блока ({data.Length} байт)");
}

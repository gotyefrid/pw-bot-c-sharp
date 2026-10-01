using System;

namespace BotCH.Core.Memory;

/// <summary>Не удалось прочитать или записать память игры (неверный адрес, процесс закрыт, нулевой указатель в цепочке).</summary>
public class MemoryAccessException : Exception
{
    public uint Address { get; }

    public MemoryAccessException(uint address, string message)
        : base($"{message} (адрес 0x{address:X8})")
    {
        Address = address;
    }
}

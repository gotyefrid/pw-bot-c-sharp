using System;
using System.Collections.Generic;
using System.Text;

namespace BotCH.Core.Memory;

/// <summary>Секция загруженного exe (из PE-заголовка).</summary>
public sealed record ModuleSection(string Name, uint Address, int Size, bool IsCode);

/// <summary>
/// Разбор PE-заголовка загруженного модуля прямо в памяти процесса — чтобы знать, где лежит код (.text)
/// и искать в нём функции по сигнатурам.
/// </summary>
public static class ModuleSections
{
    private const uint ImageScnCntCode = 0x00000020;
    private const uint ImageScnMemExecute = 0x20000000;

    public static IReadOnlyList<ModuleSection> Read(IMemory memory, uint moduleBase)
    {
        if (memory.ReadUInt16(moduleBase) != 0x5A4D) // «MZ»
            throw new MemoryAccessException(moduleBase, "Нет заголовка MZ — это не начало модуля");

        var nt = moduleBase + memory.ReadUInt32(moduleBase + 0x3C);
        if (memory.ReadUInt32(nt) != 0x00004550) // «PE\0\0»
            throw new MemoryAccessException(nt, "Нет заголовка PE");

        var sectionCount = memory.ReadUInt16(nt + 0x6);
        var optionalHeaderSize = memory.ReadUInt16(nt + 0x14);
        var table = nt + 0x18 + optionalHeaderSize;

        var sections = new List<ModuleSection>(sectionCount);
        for (var i = 0u; i < sectionCount; i++)
        {
            var header = memory.ReadBytes(table + i * 40, 40);
            var name = Encoding.ASCII.GetString(header, 0, 8).TrimEnd('\0');
            var virtualSize = BitConverter.ToInt32(header, 8);
            var virtualAddress = BitConverter.ToUInt32(header, 12);
            var characteristics = BitConverter.ToUInt32(header, 36);
            var isCode = (characteristics & (ImageScnCntCode | ImageScnMemExecute)) != 0;

            sections.Add(new ModuleSection(name, moduleBase + virtualAddress, virtualSize, isCode));
        }

        return sections;
    }

    /// <summary>
    /// Читает большой участок кусками; недоступные куски остаются нулями (защита может закрывать отдельные страницы).
    /// </summary>
    public static byte[] ReadRegion(IMemory memory, uint address, int size, int chunkSize = 0x10000)
    {
        var result = new byte[size];
        var chunk = new byte[chunkSize];

        for (var done = 0; done < size; done += chunkSize)
        {
            var count = Math.Min(chunkSize, size - done);
            if (memory.TryRead(address + (uint)done, chunk, count))
            {
                Buffer.BlockCopy(chunk, 0, result, done, count);
                continue;
            }

            // Кусок целиком не читается — пробуем по страницам
            for (var page = 0; page < count; page += 0x1000)
            {
                var pageCount = Math.Min(0x1000, count - page);
                if (memory.TryRead(address + (uint)(done + page), chunk, pageCount))
                    Buffer.BlockCopy(chunk, 0, result, done + page, pageCount);
            }
        }

        return result;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.Memory;

/// <summary>Секция загруженного exe (из PE-заголовка).</summary>
/// <param name="IsWritable">Данные, куда игра пишет (.data): там её глобальные переменные.</param>
public sealed record ModuleSection(string Name, uint Address, int Size, bool IsCode, bool IsWritable);

/// <summary>
/// Разбор PE-заголовка загруженного модуля прямо в памяти процесса — чтобы знать, где лежит код (.text)
/// и искать в нём функции по сигнатурам.
/// </summary>
public static class ModuleSections
{
    private const uint ImageScnCntCode = 0x00000020;
    private const uint ImageScnMemExecute = 0x20000000;
    private const uint ImageScnMemWrite = 0x80000000;

    public static IReadOnlyList<ModuleSection> Read(IMemory memory, uint moduleBase)
    {
        IReadOnlyList<PeSection> table;
        try
        {
            table = PeHeaders.Sections((offset, count) => memory.ReadBytes(moduleBase + offset, count));
        }
        catch (FormatException e)
        {
            throw new MemoryAccessException(moduleBase, e.Message);
        }

        return table.Select(s => new ModuleSection(s.Name, moduleBase + s.VirtualAddress, (int)s.VirtualSize,
            IsCode: (s.Characteristics & (ImageScnCntCode | ImageScnMemExecute)) != 0,
            IsWritable: (s.Characteristics & ImageScnMemWrite) != 0)).ToList();
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

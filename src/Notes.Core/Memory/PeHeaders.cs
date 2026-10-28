using System;
using System.Collections.Generic;
using System.Text;

namespace Notes.Core.Memory;

/// <summary>Строка таблицы секций PE как есть: адрес — от начала загруженного образа, смещение данных — в файле.</summary>
public sealed record PeSection(string Name, uint VirtualAddress, uint VirtualSize, uint RawSize, uint RawOffset, uint Characteristics);

/// <summary>
/// Разбор таблицы секций PE — один на exe в памяти процесса (<see cref="ModuleSections"/>) и exe-файл на диске
/// (патчер клиента). Откуда брать байты, решает вызывающий; заголовок не тот — <see cref="FormatException"/>.
/// </summary>
public static class PeHeaders
{
    /// <param name="read">Байты заголовка: (смещение от начала образа, сколько) — ровно столько байт или исключение.</param>
    public static IReadOnlyList<PeSection> Sections(Func<uint, int, byte[]> read)
    {
        var dos = read(0, 0x40);
        if (BitConverter.ToUInt16(dos, 0) != 0x5A4D) // «MZ»
            throw new FormatException("Нет заголовка MZ — это не начало exe");

        var nt = BitConverter.ToUInt32(dos, 0x3C);
        var file = read(nt, 0x18);
        if (BitConverter.ToUInt32(file, 0) != 0x00004550) // «PE\0\0»
            throw new FormatException("Нет заголовка PE");

        var count = BitConverter.ToUInt16(file, 0x6);
        var optionalHeaderSize = BitConverter.ToUInt16(file, 0x14);
        var table = read(nt + 0x18 + optionalHeaderSize, count * 40);

        var sections = new List<PeSection>(count);
        for (var i = 0; i < count; i++)
        {
            var header = i * 40;
            sections.Add(new PeSection(
                Encoding.ASCII.GetString(table, header, 8).TrimEnd('\0'),
                VirtualAddress: BitConverter.ToUInt32(table, header + 12),
                VirtualSize: BitConverter.ToUInt32(table, header + 8),
                RawSize: BitConverter.ToUInt32(table, header + 16),
                RawOffset: BitConverter.ToUInt32(table, header + 20),
                Characteristics: BitConverter.ToUInt32(table, header + 36)));
        }

        return sections;
    }
}

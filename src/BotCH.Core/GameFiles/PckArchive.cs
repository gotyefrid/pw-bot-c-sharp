using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BotCH.Core.GameFiles;

/// <summary>
/// Архив игры .pck (Angelica File Package, версия 0x00020002) — только чтение.
/// <para>
/// Хвост (0x118 байт у стандартного клиента): 0xFDFDFEEE, версия, (смещение оглавления ^ ключ смещения), 0, описание 0xFC байт,
/// 0xF00DBEEF, число файлов, версия. Число файлов и версия — всегда последние 8 байт архива.
/// Оглавление: для каждого файла (размер записи ^ Key1), (размер записи ^ Key2), запись 0x114 байт (меньше — сжата zlib):
/// путь 260 байт, смещение данных, размер, сжатый размер. Данные сжаты zlib, если сжатый размер меньше размера.
/// </para>
/// <para>
/// Клиенты отличаются ключами и размером хвоста — они в профиле сервера (<see cref="PckFormat"/>). Ещё бывает заголовок
/// в начале файла: 0x4DCA23EF, конец архива, 0x56A089B7 — тогда архив кончается там, а дальше в файле то, что клиент дописал сам.
/// </para>
/// Игра держит архив открытым, поэтому открываем с FileShare.ReadWrite.
/// </summary>
public static class PckArchive
{
    private const uint TailMagic = 0xFDFDFEEE;
    private const uint HeaderMagic1 = 0x4DCA23EF;
    private const uint HeaderMagic2 = 0x56A089B7;
    private const int TailRead = 0x10;
    private const int EntrySize = 0x114;
    private const int PathSize = 260;

    public sealed record Entry(string Path, uint Offset, int Size, int PackedSize);

    public static IReadOnlyList<Entry> List(string pckFile, PckFormat format)
    {
        using var stream = Open(pckFile);
        return ReadEntries(stream, format);
    }

    /// <summary>Содержимое первого файла, путь которого заканчивается на <paramref name="suffix"/>, или null.</summary>
    public static byte[]? ReadFile(string pckFile, string suffix, PckFormat format)
    {
        using var stream = Open(pckFile);
        foreach (var entry in ReadEntries(stream, format))
        {
            if (!entry.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                continue;

            stream.Position = entry.Offset;
            var data = ReadExactly(stream, entry.PackedSize);
            return entry.PackedSize < entry.Size ? Inflate(data) : data;
        }

        return null;
    }

    private static FileStream Open(string pckFile)
        => new(pckFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static List<Entry> ReadEntries(FileStream stream, PckFormat format)
    {
        var end = ArchiveEnd(stream);
        if (end < Math.Max(8, format.TailSize))
            throw new InvalidDataException("Файл слишком короткий для .pck");

        stream.Position = end - 8;
        var count = BitConverter.ToUInt32(ReadExactly(stream, 8), 0);

        stream.Position = end - format.TailSize;
        var tail = ReadExactly(stream, TailRead);
        var tableOffset = BitConverter.ToUInt32(tail, 8) ^ format.OffsetKey;
        if (BitConverter.ToUInt32(tail, 0) != TailMagic || tableOffset >= end)
            throw new InvalidDataException("Неизвестный формат .pck — не подходят ключи из профиля сервера (gameFiles.pck)");

        return ReadTable(stream, format, tableOffset, count);
    }

    // Заголовок «0x4DCA23EF, конец, 0x56A089B7» — архив кончается раньше конца файла
    private static long ArchiveEnd(FileStream stream)
    {
        if (stream.Length < 12)
            return stream.Length;

        stream.Position = 0;
        var header = ReadExactly(stream, 12);
        var end = BitConverter.ToUInt32(header, 4);
        return BitConverter.ToUInt32(header, 0) == HeaderMagic1 && BitConverter.ToUInt32(header, 8) == HeaderMagic2 && end <= stream.Length
            ? end
            : stream.Length;
    }

    private static List<Entry> ReadTable(FileStream stream, PckFormat format, uint tableOffset, uint count)
    {
        stream.Position = tableOffset;
        var entries = new List<Entry>((int)Math.Min(count, 100_000));
        for (var i = 0u; i < count; i++)
        {
            var header = ReadExactly(stream, 8);
            var size = BitConverter.ToUInt32(header, 0) ^ format.Key1;
            if (size != (BitConverter.ToUInt32(header, 4) ^ format.Key2) || size > 0x10000)
                throw new InvalidDataException($"Повреждённая запись оглавления №{i}");

            var raw = ReadExactly(stream, (int)size);
            var entry = size == EntrySize ? raw : Inflate(raw);

            var pathEnd = Array.IndexOf(entry, (byte)0, 0, PathSize);
            // Пути в архиве — GBK, но интересные нам (configs\skillstr.txt) в ASCII
            var path = Encoding.ASCII.GetString(entry, 0, pathEnd < 0 ? PathSize : pathEnd);
            entries.Add(new Entry(path, BitConverter.ToUInt32(entry, PathSize), BitConverter.ToInt32(entry, PathSize + 4), BitConverter.ToInt32(entry, PathSize + 8)));
        }

        return entries;
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0)
                throw new EndOfStreamException("Файл .pck обрывается");
            read += n;
        }

        return buffer;
    }

    // zlib = 2 байта заголовка + deflate; DeflateStream понимает только deflate
    private static byte[] Inflate(byte[] zlib)
    {
        using var input = new MemoryStream(zlib, 2, zlib.Length - 2);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }
}

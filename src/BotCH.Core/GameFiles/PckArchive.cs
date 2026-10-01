using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BotCH.Core.GameFiles;

/// <summary>
/// Архив игры .pck (Angelica File Package, версия 0x00020002) — только чтение.
/// <para>
/// Хвост файла (0x118 байт): 0xFDFDFEEE, версия, (смещение оглавления ^ Key1), 0, описание 0xFC байт, 0xF00DBEEF, число файлов, версия.
/// Оглавление: для каждого файла (размер записи ^ Key1), (размер записи ^ Key2), запись 0x114 байт (меньше — сжата zlib):
/// путь 260 байт, смещение данных, размер, сжатый размер. Данные сжаты zlib, если сжатый размер меньше размера.
/// </para>
/// Игра держит архив открытым, поэтому открываем с FileShare.ReadWrite.
/// </summary>
public static class PckArchive
{
    private const uint Key1 = 0xA8937462;
    private const uint Key2 = 0xF1A43653;
    private const uint TailMagic = 0xFDFDFEEE;
    private const int TailSize = 0x118;
    private const int EntrySize = 0x114;
    private const int PathSize = 260;

    public sealed record Entry(string Path, uint Offset, int Size, int PackedSize);

    public static IReadOnlyList<Entry> List(string pckFile)
    {
        using var stream = Open(pckFile);
        return ReadEntries(stream);
    }

    /// <summary>Содержимое первого файла, путь которого заканчивается на <paramref name="suffix"/>, или null.</summary>
    public static byte[]? ReadFile(string pckFile, string suffix)
    {
        using var stream = Open(pckFile);
        foreach (var entry in ReadEntries(stream))
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

    private static List<Entry> ReadEntries(FileStream stream)
    {
        if (stream.Length < TailSize)
            throw new InvalidDataException("Файл слишком короткий для .pck");

        stream.Position = stream.Length - TailSize;
        var tail = ReadExactly(stream, TailSize);
        if (BitConverter.ToUInt32(tail, 0) != TailMagic)
            throw new InvalidDataException("Неизвестный формат .pck");

        var tableOffset = BitConverter.ToUInt32(tail, 8) ^ Key1;
        var count = BitConverter.ToUInt32(tail, TailSize - 8);

        stream.Position = tableOffset;
        var entries = new List<Entry>((int)Math.Min(count, 100_000));
        for (var i = 0u; i < count; i++)
        {
            var header = ReadExactly(stream, 8);
            var size = BitConverter.ToUInt32(header, 0) ^ Key1;
            if (size != (BitConverter.ToUInt32(header, 4) ^ Key2) || size > 0x10000)
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

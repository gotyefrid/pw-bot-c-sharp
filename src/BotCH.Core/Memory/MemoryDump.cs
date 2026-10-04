using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace BotCH.Core.Memory;

/// <summary>
/// Запоминает всё, что удалось прочитать из настоящей памяти, в <see cref="MemoryImage"/>.
/// Прогнали через неё чтение снимка — получили дамп, на котором то же чтение повторяется без игры.
/// </summary>
public sealed class RecordingMemory(IMemory source) : IMemory
{
    public MemoryImage Image { get; } = new();

    public bool TryRead(uint address, byte[] buffer, int count)
    {
        if (!source.TryRead(address, buffer, count))
            return false;

        Image.TryWrite(address, count == buffer.Length ? buffer : buffer.Take(count).ToArray());
        return true;
    }

    public bool TryWrite(uint address, byte[] data) => throw new InvalidOperationException("Запись при снятии дампа запрещена");
}

/// <summary>
/// Файл дампа: сжатые gzip страницы памяти + адрес модуля и сервер. Фикстура для тестов чтения без игры.
/// </summary>
public sealed record MemoryDump(string ServerId, uint ModuleBase, MemoryImage Image)
{
    private const string Magic = "BOTCH-DUMP-1";

    public void Save(string path)
    {
        using var file = File.Create(path);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        using var writer = new BinaryWriter(gzip, Encoding.UTF8);

        writer.Write(Magic);
        writer.Write(ServerId);
        writer.Write(ModuleBase);
        var pages = Image.PageNumbers.OrderBy(p => p).ToList();
        writer.Write(pages.Count);
        foreach (var page in pages)
        {
            writer.Write(page);
            writer.Write(Image.Page(page));
        }
    }

    public static MemoryDump Load(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip, Encoding.UTF8);

        if (reader.ReadString() != Magic)
            throw new InvalidDataException("Это не дамп BotCH");

        var serverId = reader.ReadString();
        var moduleBase = reader.ReadUInt32();
        var image = new MemoryImage();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++)
        {
            var number = reader.ReadUInt32();
            var page = reader.ReadBytes(MemoryImage.PageSize);
            if (page.Length != MemoryImage.PageSize)
                throw new EndOfStreamException("Дамп обрывается");
            image.SetPage(number, page);
        }

        return new MemoryDump(serverId, moduleBase, image);
    }
}

using System;
using System.IO;
using System.Linq;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Xunit;

namespace Notes.Tests.Memory;

/// <summary>
/// Что пишется в <c>server-code-ГГГГ-ММ-ДД.log</c>: карта берётся из <see cref="MemoryImage"/>, файл — во временной папке.
/// Свои ключи (клиента) не пишутся: они не с сервера.
/// </summary>
public class ServerCodeLogTests : IDisposable
{
    private const uint Head = 0x1000;
    private const uint HeadNode = 0x2000;
    private const uint OwnKeyMin = 0xFFEA;
    private static readonly DateTime Noon = new(2026, 10, 30, 12, 0, 0);

    private readonly MemoryImage _image = new();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "notes-code-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private ServerCodeLog Watcher() => new(_image, Head, OwnKeyMin, _directory, NullLogger.Instance);

    private string Contents()
    {
        var path = Path.Combine(_directory, "server-code-2026-10-30.log");
        return File.Exists(path) ? File.ReadAllText(path) : "";
    }

    /// <summary>Узел карты: +0 левый, +4 родитель, +8 правый, +0xD «пустой», +0xE ключ, +0x10 код, +0x14 флаги.</summary>
    private void Node(uint address, uint left, uint parent, uint right, ushort key, uint code, byte flags)
    {
        var raw = new byte[0x18];
        BitConverter.GetBytes(left).CopyTo(raw, 0x00);
        BitConverter.GetBytes(parent).CopyTo(raw, 0x04);
        BitConverter.GetBytes(right).CopyTo(raw, 0x08);
        BitConverter.GetBytes(key).CopyTo(raw, 0x0E);
        BitConverter.GetBytes(code).CopyTo(raw, 0x10);
        raw[0x14] = flags;
        _image.WriteBytes(address, raw);
    }

    /// <summary>Пустая карта; дальше тест добавляет записи, переписывая корень.</summary>
    private void EmptyMap()
    {
        _image.WriteUInt32(Head, HeadNode);
        Node(HeadNode, HeadNode, HeadNode, HeadNode, 0, 0, 0);
    }

    [Fact]
    public void ServerBlockIsWrittenWithBodyAndStrings()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);

        // Тело: указатель на строку по смещению 0x10, сама строка — с 0x20
        var body = new byte[0x40];
        BitConverter.GetBytes(0x3010u).CopyTo(body, 0x00);
        BitConverter.GetBytes(0x3020u).CopyTo(body, 0x04);
        "user32.dll\0"u8.ToArray().CopyTo(body, 0x10);
        "FindWindowA\0"u8.ToArray().CopyTo(body, 0x20);
        _image.WriteBytes(0x3000, body);

        var watcher = Watcher();
        watcher.Scan(Noon);

        var text = Contents();
        Assert.Contains("новый", text);
        Assert.Contains("\t7\t0x00003000\t0x07\t", text);
        Assert.Contains("user32.dll | FindWindowA", text);
        // Тело — строкой hex: начало «10 30 00 00 20 30 00 00»
        Assert.Contains("10 30 00 00 20 30 00 00", text);
        Assert.Contains($"# Сессия {Noon:yyyy-MM-dd HH:mm:ss}: записей 1, с сервера 1, свои 0", text);
    }

    [Fact]
    public void SameBodyIsNotWrittenTwice()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xEC]);

        var watcher = Watcher();
        watcher.Scan(Noon);
        watcher.Scan(Noon.AddSeconds(2));

        Assert.Single(Contents().Split('\n'), l => l.Contains("\tновый\t"));
        Assert.DoesNotContain("\tизменился\t", Contents());
    }

    [Fact]
    public void ChangedBodyUnderSameKeyIsReported()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xEC]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // Тот же ключ, другое тело на месте — «изменился»
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xE4]);
        watcher.Scan(Noon.AddSeconds(2));

        Assert.Contains("\tизменился\t", Contents());
    }

    [Fact]
    public void MovedBlockUnderSameKeyIsReported()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xEC]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // Повторная присылка под тем же ключом: старый буфер освобождён, новый — в другом месте
        Node(0x2100, 0, HeadNode, 0, 7, 0x4000, 0x07);
        _image.WriteBytes(0x4000, [0x55, 0x8B, 0xEC]);
        watcher.Scan(Noon.AddSeconds(2));

        Assert.Contains("\tпереехал\t", Contents());
    }

    [Fact]
    public void ServiceRecordIsNotReportedAsChangedEveryTime()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        // 0x03 — служебная запись: в тех же байтах лежат рабочие данные, и они шевелятся постоянно
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x03);
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xEC]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // Данные в блоке поменялись — но это не новый код, и в файл попадать не должно
        _image.WriteBytes(0x3000, [0x11, 0x22, 0x33]);
        watcher.Scan(Noon.AddSeconds(2));
        watcher.Scan(Noon.AddSeconds(4));

        Assert.Single(Contents().Split('\n'), l => l.Contains("\tновый\t"));
        Assert.DoesNotContain("\tизменился\t", Contents());
    }

    [Fact]
    public void ExecutableBlockChangeIsStillCaught()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xEC]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // У исполняемого блока первые байты — код, и он неподвижен, поэтому изменение здесь настоящая подмена
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xE4]);
        watcher.Scan(Noon.AddSeconds(2));

        Assert.Contains("\tизменился\t", Contents());
    }

    [Fact]
    public void WorkAreaAfterTheCodeIsNotTakenForAnEdit()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);

        // Как на живом клиенте: код до +0x15, дальше дескрипторы импорта, куда клиент вписывает адреса.
        // Эти байты шевелятся постоянно, и считать их подменой кода нельзя — иначе файл забивается ложью.
        var body = new byte[0x40];
        body[0] = 0xE9;
        "user32.dll\0"u8.ToArray().CopyTo(body, 0x10);
        _image.WriteBytes(0x3000, body);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // Изменились только байты после отпечатка — события быть не должно
        body[0x20] = 0xAA;
        body[0x30] = 0xBB;
        _image.WriteBytes(0x3000, body);
        watcher.Scan(Noon.AddSeconds(2));

        Assert.DoesNotContain("\tизменился\t", Contents());
    }

    [Fact]
    public void BodyBeyondFirstPageIsNotRead()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        // Блок начинается так, что до конца страницы меньше BodyLimit, а следующая страница недоступна
        Node(0x2100, 0, HeadNode, 0, 7, 0x2FF0, 0x07);
        _image.WriteBytes(0x2FF0, new byte[0x10]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // Прочитали ровно 0x10 байт до конца страницы, а не полезли в чужую память
        Assert.Contains("\t0x00002FF0\t0x07\t16\t", Contents());
    }

    [Fact]
    public void OwnKeysAreNotWritten()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 0xFFFE, 0x3000, 0x02);
        _image.WriteBytes(0x3000, [0x55]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        Assert.DoesNotContain("\t65534\t", Contents());
    }

    [Fact]
    public void FlagsChangingWithoutNewBodyAreReportedOnce()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x03);
        _image.WriteBytes(0x3000, [0x55, 0x8B, 0xEC]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // По той же записи пришёл настоящий код: тело то же, а флаги 0x03 → 0x07
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);
        watcher.Scan(Noon.AddSeconds(2));
        watcher.Scan(Noon.AddSeconds(4));

        Assert.Single(Contents().Split('\n'), l => l.Contains("\tфлаги\t"));
    }

    [Fact]
    public void VanishedKeyIsReported()
    {
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);
        _image.WriteBytes(0x3000, [0x55]);

        var watcher = Watcher();
        watcher.Scan(Noon);

        // Карта опустела (клиент переподключился) — старый ключ больше не виден
        EmptyMap();
        watcher.Scan(Noon.AddSeconds(2));

        Assert.Contains("\tпропал\t", Contents());
    }

    [Fact]
    public void EmptyMapStillLeavesAHeaderSoItIsClearTheLogWorks()
    {
        EmptyMap();
        var watcher = Watcher();

        watcher.Scan(Noon);

        var text = Contents();
        Assert.Contains("# Сессия 2026-10-30 12:00:00: записей 0, с сервера 0, свои 0", text);
        // Заголовок один: второй проход без событий ничего не добавляет
        watcher.Scan(Noon.AddSeconds(2));
        Assert.Single(Contents().Split('\n'), l => l.StartsWith("# Сессия", StringComparison.Ordinal));
    }

    [Fact]
    public void NoOffsetsMeansNoWatcher()
    {
        Assert.Null(ServerCodeLog.Start(_image, 0x400000, null, _directory, NullLogger.Instance));
        Assert.Null(ServerCodeLog.Start(_image, 0x400000, new Notes.Core.Profiles.ServerCodeOffsets(), _directory,
            NullLogger.Instance));
    }

    [Fact]
    public void MapHeadIsTakenFromTheModuleBaseNotFromTheProfile()
    {
        // В профиле смещение от начала модуля (у клиента ASLR); читать надо базу + смещение.
        // Ошибка, которую этот тест закрывает: карту искали по самому смещению, и на живом клиенте она «не читалась».
        var offsets = new Notes.Core.Profiles.ServerCodeOffsets { MapHead = 0x1000, OwnKeyMin = OwnKeyMin };
        EmptyMap();
        Node(HeadNode, 0x2100, 0x2100, 0x2100, 0, 0, 0);
        Node(0x2100, 0, HeadNode, 0, 7, 0x3000, 0x07);
        _image.WriteBytes(0x3000, [0x55]);

        // База 0 — карта по адресу 0x1000, как в профиле; с ненулевой базой читалось бы мимо
        using var watcher = ServerCodeLog.Start(_image, 0x0, offsets, _directory, NullLogger.Instance);

        Assert.NotNull(watcher);
        watcher!.Scan(Noon);
        Assert.Contains("\tновый\t", Contents());
    }

    [Fact]
    public void BadMapHeadThrowsAndLoopIsWhatSwallowsIt()
    {
        // По адресу головы ничего нет — смещение в профиле не от этого клиента.
        // Scan честно бросает, а глушит это поток (Loop) — иначе ошибка профиля прошла бы молча.
        var watcher = Watcher();

        Assert.Throws<MemoryAccessException>(() => watcher.Scan(Noon));
    }
}

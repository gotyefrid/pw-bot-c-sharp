using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Notes.Core.Logging;

/// <summary>
/// Файл лога на каждый день. Пока персонаж неизвестен — общий <c>logs\notes-2026-10-01.log</c>; подключились к персонажу
/// (<see cref="SetCharacter"/>) — его файл <c>logs\&lt;сервер&gt;\2026-10-01 Ник.log</c>: у нескольких копий бота из одной папки
/// строки не перемешиваются. Файл открывается на дозапись и сразу закрывается — его можно читать и удалять, пока бот работает.
/// </summary>
public sealed class DailyFileSink(string directory, string prefix = "notes") : ILogSink
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private sealed record Character(string Server, string Nick);

    // Пишут из разных потоков: ссылка меняется целиком
    private volatile Character? _character;

    public string Directory { get; } = directory;

    /// <summary>Дальше писать в файл этого персонажа (сервер — id профиля); null — в общий.</summary>
    public void SetCharacter(string? server, string? nick)
        => _character = string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(nick) ? null : new Character(server!, nick!);

    public string PathFor(DateTime day)
    {
        var date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return _character is { } c
            ? Path.Combine(Directory, SafeName(c.Server), $"{date} {SafeName(c.Nick)}.log")
            : Path.Combine(Directory, $"{prefix}-{date}.log");
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public void Write(LogEntry entry)
    {
        var path = PathFor(entry.Time);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream, Utf8);
        writer.WriteLine(entry.ToString());
    }
}

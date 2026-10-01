using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace BotCH.Core.Logging;

/// <summary>
/// Файл лога на каждый день: <c>logs\botch-2026-10-01.log</c>. Файл открывается на дозапись и сразу закрывается —
/// его можно читать и удалять, пока бот работает.
/// </summary>
public sealed class DailyFileSink(string directory, string prefix = "botch") : ILogSink
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public string Directory { get; } = directory;

    public string PathFor(DateTime day)
        => Path.Combine(Directory, $"{prefix}-{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");

    public void Write(LogEntry entry)
    {
        System.IO.Directory.CreateDirectory(Directory);
        using var stream = new FileStream(PathFor(entry.Time), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream, Utf8);
        writer.WriteLine(entry.ToString());
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace BotCH.Core.Logging;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

public sealed record LogEntry(DateTime Time, LogLevel Level, string Source, string Message)
{
    public override string ToString()
        => $"{Time.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} {LevelMark(Level)} [{Source}] {Message}";

    private static string LevelMark(LogLevel level) => level switch
    {
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warning => "WRN",
        _ => "ERR",
    };
}

/// <summary>Куда уходят записи лога: окно, файл, тест.</summary>
public interface ILogSink
{
    void Write(LogEntry entry);
}

public interface ILogger
{
    void Log(LogLevel level, string message);
}

/// <summary>
/// Лог бота. Источник («пет», «бой»…) задаётся через <see cref="For"/>, записи раздаются всем приёмникам.
/// Ошибка одного приёмника (например, файл занят) не ломает остальные и не роняет бота.
/// </summary>
public sealed class Logger
{
    private readonly List<ILogSink> _sinks = [];
    private readonly object _lock = new();
    private readonly Func<DateTime> _clock;

    public Logger(Func<DateTime>? clock = null) => _clock = clock ?? (() => DateTime.Now);

    /// <summary>Записи ниже этого уровня отбрасываются.</summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Info;

    public Logger AddSink(ILogSink sink)
    {
        lock (_lock)
            _sinks.Add(sink);
        return this;
    }

    public ILogger For(string source) => new SourceLogger(this, source);

    public void Log(LogLevel level, string source, string message)
    {
        if (level < MinLevel)
            return;

        var entry = new LogEntry(_clock(), level, source, message);
        lock (_lock)
        {
            foreach (var sink in _sinks)
            {
                try
                {
                    sink.Write(entry);
                }
                catch
                {
                    // Лог не должен останавливать бота
                }
            }
        }
    }

    private sealed class SourceLogger(Logger owner, string source) : ILogger
    {
        public void Log(LogLevel level, string message) => owner.Log(level, source, message);
    }
}

public static class LoggerExtensions
{
    public static void Debug(this ILogger logger, string message) => logger.Log(LogLevel.Debug, message);
    public static void Info(this ILogger logger, string message) => logger.Log(LogLevel.Info, message);
    public static void Warning(this ILogger logger, string message) => logger.Log(LogLevel.Warning, message);
    public static void Error(this ILogger logger, string message) => logger.Log(LogLevel.Error, message);
}

/// <summary>Лог, который никуда не пишет — для мест, где лог не важен.</summary>
public sealed class NullLogger : ILogger
{
    public static readonly NullLogger Instance = new();

    public void Log(LogLevel level, string message)
    {
    }
}

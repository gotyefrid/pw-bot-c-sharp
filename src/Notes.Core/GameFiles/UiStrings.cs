using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Notes.Core.GameFiles;

/// <summary>
/// Строки окон игры: element\interfaces\ingame.stf (UTF-16), каждая — «номер, табуляция, "текст"», текст — шаблон printf
/// (%s, %d); например 233 — «Вы говорите с &amp;%s&amp;: %s». Так игра пишет у себя то, чего сервер не присылает.
/// </summary>
public sealed class UiStrings
{
    /// <summary>Файл строк окон мира — от папки клиента (там, где ElementClient.exe).</summary>
    public const string InGameFile = @"interfaces\ingame.stf";

    private static readonly Regex Line = new("^\\s*(\\d+)\\s+\"(.*)\"\\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly Dictionary<uint, string> _strings;

    private UiStrings(Dictionary<uint, string> strings) => _strings = strings;

    public int Count => _strings.Count;

    public string? Get(uint id) => _strings.TryGetValue(id, out var text) ? text : null;

    public static UiStrings Parse(string text)
    {
        var strings = new Dictionary<uint, string>();
        foreach (Match match in Line.Matches(text))
            if (uint.TryParse(match.Groups[1].Value, out var id))
                strings[id] = match.Groups[2].Value;
        return new UiStrings(strings);
    }

    /// <summary>Строки окон мира из папки клиента; не прочитались — null и причина в <paramref name="problem"/>.</summary>
    public static UiStrings? LoadFromGameDirectory(string gameDirectory, out string? problem)
    {
        var path = Path.Combine(gameDirectory, InGameFile);
        try
        {
            // Файл держит клиент — открывать с общим доступом
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.Unicode, detectEncodingFromByteOrderMarks: true);
            problem = null;
            return Parse(reader.ReadToEnd());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            problem = $"не прочитать {path}: {e.Message}";
            return null;
        }
    }
}

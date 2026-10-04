using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BotCH.Core.GameFiles;

/// <summary>
/// Названия скиллов из файла игры: element\configs.pck → configs\skillstr.txt (UTF-16).
/// Строки вида <c>2990  "Жалящий рой"</c>: ключ ID*10 — название, ID*10+1 — описание.
/// </summary>
public sealed class SkillNames
{
    private static readonly string[] Archives = ["configs.pck", "configs2.pck"];
    private static readonly Regex Line = new("^\\s*(\\d+)\\s+\"([^\"]*)\"", RegexOptions.Multiline | RegexOptions.Compiled);

    private readonly Dictionary<int, string> _names;

    private SkillNames(Dictionary<int, string> names, string source)
    {
        _names = names;
        Source = source;
    }

    public static SkillNames Empty { get; } = new([], "");

    /// <summary>Откуда прочитаны названия (путь к архиву); пусто — не прочитаны.</summary>
    public string Source { get; }

    public int Count => _names.Count;

    public string? Get(int skillId) => _names.TryGetValue(skillId, out var name) ? name : null;

    /// <summary>
    /// Читает названия из папки клиента (там, где ElementClient.exe): configs.pck, если там нет — configs2.pck.
    /// Не получилось — <see cref="Empty"/> и причина в <paramref name="problem"/>.
    /// </summary>
    /// <param name="format">Ключи .pck этого клиента — из профиля сервера.</param>
    public static SkillNames LoadFromGameDirectory(string gameDirectory, PckFormat format, out string? problem)
    {
        problem = "Не найдены configs.pck/configs2.pck в " + gameDirectory;
        foreach (var archive in Archives)
        {
            var path = Path.Combine(gameDirectory, archive);
            if (!File.Exists(path))
                continue;

            try
            {
                var data = PckArchive.ReadFile(path, "\\skillstr.txt", format);
                if (data is null)
                {
                    problem = $"В {archive} нет skillstr.txt";
                    continue;
                }

                problem = null;
                return new SkillNames(Parse(Encoding.Unicode.GetString(data)), path);
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                problem = $"{archive}: {e.Message}";
            }
        }

        return Empty;
    }

    internal static Dictionary<int, string> Parse(string text)
    {
        var names = new Dictionary<int, string>();
        // Значение в кавычках может занимать несколько строк (у описаний), поэтому разбираем весь текст сразу
        foreach (Match match in Line.Matches(text))
        {
            if (int.TryParse(match.Groups[1].Value, out var key) && key % 10 == 0)
                names[key / 10] = match.Groups[2].Value;
        }

        return names;
    }
}

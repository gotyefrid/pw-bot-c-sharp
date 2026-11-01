using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.GameFiles;

/// <summary>
/// Текст дополнительного свойства вещи, как в подсказке игры («Магическая атака +29»). Номер свойства → тип — из файла игры
/// element\configs.pck → configs\item_ext_prop.txt (блоки <c>type: 11 { 441, 442, … }</c>); тип → фраза и как подставить
/// числа — из профиля сервера (<see cref="GameFilesData.AddonTexts"/>: это зашито в коде клиента).
/// </summary>
public sealed class ItemAddonTexts
{
    private static readonly string[] Archives = ["configs.pck", "configs2.pck"];
    private static readonly Regex TypeBlock = new(@"type:\s*(\d+)\s*\{([^}]*)\}", RegexOptions.Compiled);
    private static readonly Regex Comment = new(@"/\*.*?\*/|//[^\n]*", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Spec = new(@"%%|%(\+?)(?:\.(\d+))?([df])", RegexOptions.Compiled);

    private readonly Dictionary<int, int> _types;
    private readonly IReadOnlyDictionary<int, List<string>> _texts;

    public ItemAddonTexts(Dictionary<int, int> types, IReadOnlyDictionary<int, List<string>> texts)
    {
        _types = types;
        _texts = texts;
    }

    public static ItemAddonTexts Empty { get; } = new([], new Dictionary<int, List<string>>());

    /// <summary>Сколько номеров свойств знаем (0 — файл игры не прочитан).</summary>
    public int Count => _types.Count;

    /// <summary>Тип свойства (как в item_ext_prop.txt); null — номер неизвестен.</summary>
    public int? TypeOf(int addonId) => _types.TryGetValue(addonId, out var type) ? type : null;

    /// <summary>Текст свойства как в подсказке; неизвестно — «свойство N: числа».</summary>
    public string Describe(ItemAddon addon)
    {
        if (TypeOf(addon.Id) is { } type && _texts.TryGetValue(type, out var text) && text.Count > 0
            && Format(text[0], text.Skip(1).ToList(), addon.Params) is { } done)
            return done;
        return $"свойство {addon.Id}: {string.Join("/", addon.Params)}";
    }

    /// <summary>Читает типы свойств из папки клиента; не получилось — <see cref="Empty"/> и причина.</summary>
    public static ItemAddonTexts LoadFromGameDirectory(string gameDirectory, GameFilesData files, out string? problem)
    {
        problem = "Не найдены configs.pck/configs2.pck в " + gameDirectory;
        if (files.AddonTexts.Count == 0)
        {
            problem = "в профиле нет текстов свойств (gameFiles.addonTexts)";
            return Empty;
        }

        foreach (var archive in Archives)
        {
            var path = Path.Combine(gameDirectory, archive);
            if (!File.Exists(path))
                continue;

            try
            {
                var data = PckArchive.ReadFile(path, "\\item_ext_prop.txt", files.Pck);
                if (data is null)
                {
                    problem = $"В {archive} нет item_ext_prop.txt";
                    continue;
                }

                problem = null;
                // Комментарии в файле — по-китайски (GBK); нам нужны только числа, поэтому кодировка не важна
                return new ItemAddonTexts(ParseTypes(Encoding.GetEncoding(28591).GetString(data)), files.AddonTexts);
            }
            catch (Exception e) when (e is IOException or InvalidDataException)
            {
                problem = $"{archive}: {e.Message}";
            }
        }

        return Empty;
    }

    internal static Dictionary<int, int> ParseTypes(string text)
    {
        var types = new Dictionary<int, int>();
        foreach (Match block in TypeBlock.Matches(Comment.Replace(text, "")))
        {
            var type = int.Parse(block.Groups[1].Value, CultureInfo.InvariantCulture);
            foreach (Match id in Regex.Matches(block.Groups[2].Value, @"\d+"))
                types[int.Parse(id.Value, CultureInfo.InvariantCulture)] = type;
        }

        return types;
    }

    /// <summary>
    /// Подставить числа свойства в фразу клиента (%+d, %d, %+.2f, %.2f, %%). Как получить число — по <paramref name="args"/>:
    /// p0…p2 — число как есть, «-» впереди — с обратным знаком, «f» — в памяти дробное (float), «*100» — умножить и округлить,
    /// «*0.05» — умножить, «/2» — разделить нацело (дробь отбросить). null — чисел не хватает или вид неизвестен.
    /// </summary>
    internal static string? Format(string format, IReadOnlyList<string> args, IReadOnlyList<int> values)
    {
        var next = 0;
        var failed = false;
        var result = Spec.Replace(format, m =>
        {
            if (m.Value == "%%")
                return "%";
            if (next >= args.Count || Value(args[next++], values) is not { } value)
            {
                failed = true;
                return "";
            }

            var sign = m.Groups[1].Value == "+" && value >= 0 ? "+" : "";
            return m.Groups[3].Value == "d"
                ? sign + ((long)Math.Round(value, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture)
                : sign + value.ToString("F" + (m.Groups[2].Success ? m.Groups[2].Value : "6"), CultureInfo.InvariantCulture);
        });
        return failed ? null : result;
    }

    private static readonly Regex Arg = new(@"^(-?)p([0-2])(f?)(?:\*([\d.]+)|/(\d+))?$", RegexOptions.Compiled);

    private static double? Value(string arg, IReadOnlyList<int> values)
    {
        var m = Arg.Match(arg);
        var index = m.Success ? m.Groups[2].Value[0] - '0' : -1;
        if (index < 0 || index >= values.Count)
            return null;

        double value = m.Groups[3].Value == "f" ? BitConverter.ToSingle(BitConverter.GetBytes(values[index]), 0) : values[index];
        if (m.Groups[4].Success)
            value *= double.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
        if (m.Groups[5].Success)
            value = Math.Truncate(value / int.Parse(m.Groups[5].Value, CultureInfo.InvariantCulture));
        return m.Groups[1].Value == "-" ? -value : value;
    }
}

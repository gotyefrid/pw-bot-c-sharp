using System.Collections.Generic;
using System.Linq;

namespace Notes.App;

/// <summary>
/// Цвета значка бота на панели задач: 12 заметно разных, тёмных настолько, чтобы белые буквы читались. Без выбора — цвет по нику
/// из этой же палитры (похожих оттенков у двух ботов не бывает, совпасть могут — тогда цвет выбирают в окне).
/// </summary>
public static class IconPalette
{
    public sealed record Choice(string Name, string Hex);

    public static IReadOnlyList<Choice> Colors { get; } =
    [
        new("красный", "#D32F2F"),
        new("оранжевый", "#E65100"),
        new("янтарный", "#B7791F"),
        new("салатовый", "#558B2F"),
        new("зелёный", "#1B7A3A"),
        new("бирюзовый", "#00897B"),
        new("голубой", "#0277BD"),
        new("синий", "#283593"),
        new("фиолетовый", "#6A1B9A"),
        new("розовый", "#C2185B"),
        new("коричневый", "#6D4C41"),
        new("серый", "#546E7A"),
    ];

    /// <summary>Цвет ника из палитры: по хешу имени (свой, а не string.GetHashCode — тот может меняться между версиями .NET).</summary>
    public static string Auto(string nick)
    {
        uint hash = 2166136261;
        foreach (var ch in nick)
            hash = (hash ^ ch) * 16777619;
        return Colors[(int)(hash % (uint)Colors.Count)].Hex;
    }

    /// <summary>Название цвета для подсказки; не из палитры — сам код.</summary>
    public static string NameOf(string hex) => Colors.FirstOrDefault(c => c.Hex == hex)?.Name ?? hex;
}

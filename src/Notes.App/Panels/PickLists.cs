using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Clicker;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Варианты для выбора названий (мобы, лут, ресурсы): сначала то, что вокруг сейчас, потом встреченное раньше, в конце — группы
/// (лут: оружие, броня, банки, предметы ДК…).
/// </summary>
public static class PickLists
{
    public static IReadOnlyList<PickOption> Options(IReadOnlyList<NameCount> nearby, IEnumerable<NameCount> seen, IEnumerable<string>? groups = null)
    {
        var options = nearby.Select(n => new PickOption(n.Name, n.ToString())).ToList();
        var near = new HashSet<string>(nearby.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        options.AddRange(seen.Where(n => !near.Contains(n.Name)).OrderBy(n => n.Name).Select(n => new PickOption(n.Name, $"{n} — не рядом")));
        options.AddRange((groups ?? []).Select(g => new PickOption(g, $"{g} — все предметы группы")));
        return options;
    }

    /// <summary>Виды действий или условий кликера — для выбора с поиском: имя — вид в файле, текст — название в окне.</summary>
    /// <summary>Корм пета из сумки: «любой» и названия со счётом, сначала самый простой (меньше даёт верности).</summary>
    public static List<PickOption> Foods(IEnumerable<InventoryItem> bag)
    {
        var list = new List<PickOption> { new("", "любой — сначала самый простой") };
        list.AddRange(bag.Where(i => i.IsPetFood && i.Name is not null)
            .GroupBy(i => i.Name!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(i => i.FoodLoyalty))
            .Select(g => new PickOption(g.Key, $"{g.Key} ×{g.Sum(i => i.Count)}")));
        return list;
    }

    /// <summary>Изученные скиллы персонажа по названиям из игры (ещё не загрузились — «скилл 123»); имя варианта — ID.</summary>
    public static List<PickOption> Skills(IEnumerable<SkillInfo> skills)
        => skills.Select(s => new PickOption(s.Id.ToString(), s.Name ?? $"скилл {s.Id}")).ToList();

    public static IReadOnlyList<PickOption> Kinds<T>(ClickerKindList<T> kinds) where T : class
        => kinds.All.Select(k => new PickOption(k.Id, k.Title)).ToList();
}

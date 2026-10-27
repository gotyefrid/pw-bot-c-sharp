using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.World;

namespace BotCH.App.Panels;

/// <summary>
/// Варианты для выбора названий (мобы, лут, ресурсы): сначала то, что вокруг сейчас, потом встреченное раньше, в конце — группы
/// (лут: «Предметы ДК»).
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
}

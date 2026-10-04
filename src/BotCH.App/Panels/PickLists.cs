using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.World;

namespace BotCH.App.Panels;

/// <summary>Варианты для выбора названий (мобы, лут, ресурсы): сначала то, что вокруг сейчас, потом встреченное раньше.</summary>
public static class PickLists
{
    public static IReadOnlyList<PickOption> Options(IReadOnlyList<NameCount> nearby, IEnumerable<NameCount> seen)
    {
        var options = nearby.Select(n => new PickOption(n.Name, n.ToString())).ToList();
        var near = new HashSet<string>(nearby.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        options.AddRange(seen.Where(n => !near.Contains(n.Name)).OrderBy(n => n.Name).Select(n => new PickOption(n.Name, $"{n} — не рядом")));
        return options;
    }
}

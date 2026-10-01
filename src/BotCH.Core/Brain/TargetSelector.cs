using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>Выбор цели.</summary>
public static class TargetSelector
{
    /// <summary>Живой моб, который бьёт перса или пета (ближайший из таких). Белый список для него не важен.</summary>
    public static NpcInfo? Aggressor(WorldState w)
    {
        var petWid = w.Pet?.ActiveWid ?? 0;
        return w.Mobs
            .Where(m => !m.IsDead && m.TargetWid != 0 && (m.TargetWid == w.Host.Wid || m.TargetWid == petWid))
            .OrderBy(m => m.Distance)
            .FirstOrDefault();
    }

    /// <summary>Можно ли нападать на этого моба по настройкам (жив, моб, название из списка).</summary>
    public static bool IsAllowed(NpcInfo mob, TargetSettings target)
        => mob.Kind == NpcKind.Mob && !mob.IsDead && MobNameFilter.Allows(target, mob.Name);

    /// <summary>Ближайший живой моб, разрешённый настройками и в радиусе фарма, кроме брошенных.</summary>
    public static NpcInfo? Nearest(WorldState w, TargetSettings target, ICollection<uint> skip, Func<NpcInfo, bool>? inArea = null)
        => w.Mobs
            .Where(m => IsAllowed(m, target) && !skip.Contains(m.Wid) && (inArea?.Invoke(m) ?? true))
            .OrderBy(m => m.Distance)
            .FirstOrDefault();
}

using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>Насколько моб опасен для нас — по нему выбирается, с кем драться. Больше — важнее.</summary>
public enum Threat
{
    /// <summary>Никого из нас не бьёт (или правило «сначала тех, кто бьёт» выключено).</summary>
    None,
    /// <summary>Бьёт перса или пета.</summary>
    HitsUs,
    /// <summary>Бьёт самого перса, а пет может его снять (правило «снимать мобов с меня петом»).</summary>
    HitsMe,
}

/// <summary>Выбор цели.</summary>
public static class TargetSelector
{
    /// <summary>Опасность моба при этих правилах.</summary>
    /// <param name="hitsUsFirst">«Сначала тех, кто бьёт меня или пета».</param>
    /// <param name="petTakesAggro">«Снимать мобов с меня петом» (и пет призван).</param>
    public static Threat ThreatOf(NpcInfo mob, WorldState w, bool hitsUsFirst, bool petTakesAggro)
    {
        // Возвращается — неуязвим и уже не наш (цель может ещё показывать нас)
        if (mob.IsDead || mob.TargetWid == 0 || mob.Returning)
            return Threat.None;
        if (petTakesAggro && mob.TargetWid == w.Host.Wid)
            return Threat.HitsMe;

        var petWid = w.Pet?.ActiveWid ?? 0;
        return hitsUsFirst && (mob.TargetWid == w.Host.Wid || mob.TargetWid == petWid) ? Threat.HitsUs : Threat.None;
    }

    /// <summary>Самый опасный моб: сначала по <see cref="Threat"/>, при равной — ближайший. null — никто из мобов нас не бьёт.</summary>
    public static NpcInfo? MostDangerous(WorldState w, bool hitsUsFirst, bool petTakesAggro, out Threat threat)
    {
        var best = w.Mobs
            .Select(m => (Mob: m, Threat: ThreatOf(m, w, hitsUsFirst, petTakesAggro)))
            .Where(x => x.Threat != Threat.None)
            .OrderByDescending(x => x.Threat)
            .ThenBy(x => x.Mob.Distance)
            .FirstOrDefault();
        threat = best.Threat;
        return best.Mob;
    }

    /// <summary>Живой моб, который бьёт перса или пета (ближайший из таких). Белый список для него не важен.</summary>
    public static NpcInfo? Aggressor(WorldState w)
    {
        var petWid = w.Pet?.ActiveWid ?? 0;
        return w.Mobs
            .Where(m => !m.IsDead && !m.Returning && m.TargetWid != 0 && (m.TargetWid == w.Host.Wid || m.TargetWid == petWid))
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

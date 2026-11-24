using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>Насколько моб опасен для нас — по нему выбирается, с кем драться. Больше — важнее.</summary>
public enum Threat
{
    /// <summary>Никого из нас не бьёт (или правило «сначала тех, кто бьёт» выключено).</summary>
    None,
    /// <summary>Бьёт перса или пета.</summary>
    HitsUs,
    /// <summary>Бьёт пета, а пет его ещё ни разу не ударил: лечение пета переведёт его на перса — пет должен ударить его первым.</summary>
    PetUntouched,
    /// <summary>Бьёт самого перса, а пет может его снять (правило «снимать мобов с меня петом»).</summary>
    HitsMe,
}

/// <summary>Выбор цели.</summary>
public static class TargetSelector
{
    /// <summary>Опасность моба при этих правилах.</summary>
    /// <param name="hitsUsFirst">«Сначала тех, кто бьёт меня или пета».</param>
    /// <param name="petTakesAggro">«Снимать мобов с меня петом» (и пет призван).</param>
    /// <param name="petHit">Пет в бою: ударил ли он уже этого моба (<see cref="PetTags"/>); null — пета нет, правила пета не действуют.</param>
    public static Threat ThreatOf(NpcInfo mob, WorldState w, bool hitsUsFirst, bool petTakesAggro, Func<uint, bool>? petHit = null)
    {
        if (!w.AttacksUs(mob))
            return Threat.None;
        if (petTakesAggro && mob.TargetWid == w.Host.Wid)
            return Threat.HitsMe;
        if (!hitsUsFirst)
            return Threat.None;

        return petHit is not null && mob.TargetWid == w.Pet?.ActiveWid && !petHit(mob.Wid) ? Threat.PetUntouched : Threat.HitsUs;
    }

    /// <summary>
    /// Важнее ли <paramref name="mob"/>, чем <paramref name="other"/>: сначала по <see cref="Threat"/>, при равной — ближайший (по
    /// прямой, с высотой). С петом (<paramref name="weakestFirst"/>) среди тех, кого пет уже держит (<see cref="Threat.HitsUs"/>), —
    /// у кого меньше HP: добить скорее, чтобы меньше бил.
    /// </summary>
    public static bool Before(NpcInfo mob, Threat threat, NpcInfo other, Threat otherThreat, bool weakestFirst)
    {
        if (threat != otherThreat)
            return threat > otherThreat;
        if (weakestFirst && threat == Threat.HitsUs && mob.Hp > 0 && other.Hp > 0 && mob.Hp != other.Hp)
            return mob.Hp < other.Hp;
        return mob.Offset.Direct < other.Offset.Direct;
    }

    /// <summary>
    /// Бросить текущего ради другого: другой опаснее, а с петом при равной — он слабее (из тех, кого пет держит). Просто ближе — не
    /// повод: иначе бой мечется между двумя.
    /// </summary>
    public static bool Switch(NpcInfo other, Threat otherThreat, NpcInfo current, Threat currentThreat, bool weakestFirst)
        => otherThreat > currentThreat
           || weakestFirst && otherThreat == currentThreat && otherThreat == Threat.HitsUs && other.Hp > 0 && current.Hp > 0 && other.Hp < current.Hp;

    /// <summary>Самый опасный моб (<see cref="Before"/>). null — никто из мобов нас не бьёт.</summary>
    public static NpcInfo? MostDangerous(WorldState w, bool hitsUsFirst, bool petTakesAggro, out Threat threat, Func<uint, bool>? petHit = null)
    {
        threat = Threat.None;
        NpcInfo? best = null;
        foreach (var mob in w.Mobs)
        {
            var t = ThreatOf(mob, w, hitsUsFirst, petTakesAggro, petHit);
            if (t != Threat.None && (best is null || Before(mob, t, best, threat, petHit is not null)))
                (best, threat) = (mob, t);
        }

        return best;
    }

    /// <summary>
    /// Живой моб, который напал на перса или пета (<see cref="WorldState.AttacksUs"/>). Ближайший из таких (по прямой, с высотой).
    /// Белый список для него не важен.
    /// </summary>
    public static NpcInfo? Aggressor(WorldState w)
        => w.Mobs
            .Where(w.AttacksUs)
            .OrderBy(m => m.Offset.Direct)
            .FirstOrDefault();

    /// <summary>Можно ли нападать на этого моба по настройкам (жив, моб, название из списка).</summary>
    public static bool IsAllowed(NpcInfo mob, TargetSettings target)
        => mob.Kind == NpcKind.Mob && !mob.IsDead && MobNameFilter.Allows(target, mob.Name);

    /// <summary>
    /// Ближайший живой моб, разрешённый настройками и в радиусе фарма, кроме брошенных. Ближайший — по прямой, с высотой: моб
    /// в 10 м по карте, но на 29 м выше (летает), дальше моба в 15 м на земле.
    /// </summary>
    public static NpcInfo? Nearest(WorldState w, TargetSettings target, ICollection<uint> skip, Func<NpcInfo, bool>? inArea = null)
        => w.Mobs
            .Where(m => IsAllowed(m, target) && !skip.Contains(m.Wid) && (inArea?.Invoke(m) ?? true))
            .OrderBy(m => m.Offset.Direct)
            .FirstOrDefault();
}

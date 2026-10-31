using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Подробный лог нападений: моб взял в цель перса или пета и действует (<see cref="NpcInfo.Engaging"/>) — кто, на кого, что
/// делает, где; что решил бой и почему (решение даёт хозяин); через сколько бой перешёл на него; когда отстал. Пишет только
/// перемены, не каждый снимок. Моб, с которым бой начали сами, — не нападение: он просто отвечает, о нём молчим.
/// </summary>
public sealed class AttackWatch
{
    private sealed class Attacker(string name, DateTime since, bool ours)
    {
        public string Name { get; } = name;
        public DateTime Since { get; } = since;
        public bool Ours { get; } = ours;
        public DateTime? FirstHit { get; set; }
        public string? Verdict { get; set; }
    }

    private readonly Dictionary<uint, Attacker> _attackers = [];

    /// <param name="verdict">Что бой делает с этим мобом и почему: «беру в цель», «сначала добиваю …».</param>
    public void Observe(BrainContext c, Func<NpcInfo, string> verdict)
    {
        var w = c.World;
        foreach (var mob in w.Mobs.Where(w.TargetsUs))
        {
            if (!_attackers.TryGetValue(mob.Wid, out var attacker))
            {
                if (!mob.Engaging)
                    continue;

                var ours = InFight(c, mob) || w.Host.TargetWid == mob.Wid;
                attacker = new Attacker(mob.Name, c.Now, ours);
                _attackers[mob.Wid] = attacker;
                if (!ours)
                    c.Log.Info($"Нападение: {mob.Name} 0x{mob.Wid:X8} {Whom(w, mob)}, {Doing(mob)}, {mob.Offset}{(mob.Hp > 0 ? $", HP {mob.Hp}" : "")}; {Now(c)}");
            }

            if (attacker.FirstHit is null && HitsUs(w, mob))
                attacker.FirstHit = c.Now;
            if (attacker.Ours)
                continue;

            var now = verdict(mob);
            if (now == attacker.Verdict)
                continue;

            attacker.Verdict = now;
            c.Log.Info($"{mob.Name}: {now}{(InFight(c, mob) ? Reaction(c, attacker) : "")}");
        }

        foreach (var gone in _attackers.Where(a => w.Npcs.FirstOrDefault(n => n.Wid == a.Key) is not { } n || !w.TargetsUs(n)).ToList())
        {
            _attackers.Remove(gone.Key);
            if (!gone.Value.Ours)
                c.Log.Info($"{gone.Value.Name}: {Left(w, gone.Key)}");
        }
    }

    public void Reset() => _attackers.Clear();

    private static bool InFight(BrainContext c, NpcInfo mob) => c.Fight.Phase == FightPhase.Fighting && c.Fight.Mob == mob.Wid;

    private static string Whom(WorldState w, NpcInfo mob) => mob.TargetWid == w.Host.Wid ? "→ на меня" : "→ на пета";

    private static string Doing(NpcInfo mob) => mob.Approaching ? "идёт к нам" : mob.State.Text() ?? "?";

    private static string Now(BrainContext c) => c.Fight.Phase switch
    {
        FightPhase.Fighting => $"сейчас бой: {c.Fight.MobName}",
        FightPhase.Looting => "сейчас лут",
        _ => "боя нет",
    };

    // Его удар с уроном по персу или пету — в списке ударов, который показывает игра
    private static bool HitsUs(WorldState w, NpcInfo mob)
    {
        var pet = w.Pet is { IsSummoned: true } p ? p.ActiveWid : 0;
        return w.Attacks.Any(a => a.AttackerWid == mob.Wid
                                  && a.Hits.Any(h => h.Damage > 0 && (h.TargetWid == w.Host.Wid || pet != 0 && h.TargetWid == pet)));
    }

    private static string Reaction(BrainContext c, Attacker attacker)
        => $" — через {(c.Now - attacker.Since).TotalSeconds:0.0} с после нападения"
           + (attacker.FirstHit is { } hit ? $", {(c.Now - hit).TotalSeconds:0.0} с после первого удара" : ", ударов по нам не видели");

    private static string Left(WorldState w, uint wid) => w.Npcs.FirstOrDefault(n => n.Wid == wid) switch
    {
        null => "пропал из виду",
        { IsDead: true } => "убит",
        { Returning: true } => "бросил нас, уходит на место",
        _ => "отстал: сменил цель",
    };
}

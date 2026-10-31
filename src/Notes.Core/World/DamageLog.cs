using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Logging;

namespace Notes.Core.World;

/// <summary>
/// Лог урона: удары персонажа и пета по мобам и удары по персонажу и пету — с HP после удара. Смотрит каждый снимок, и когда
/// бот не запущен. Удары без урона (бафы, лечение, промахи) и чужие бои не пишет. HP после удара — HP до удара минус урон:
/// игра снижает HP на 0,3–0,4 с позже, чем удар появляется в списке. Ещё пишет, когда другой игрок берёт в цель персонажа
/// или пета: это видно раньше первого удара.
/// </summary>
public sealed class DamageLog(ILogger log)
{
    private readonly AttackTracker _tracker = new();
    // Игроки, у которых в цели персонаж или пет (WID игрока → WID цели): пишем, только когда цель появилась или сменилась
    private Dictionary<uint, uint> _aiming = [];

    public void Observe(WorldState world)
    {
        ObservePlayers(world);
        foreach (var attack in _tracker.Update(world))
        {
            foreach (var hit in attack.Hits.Where(h => h.Damage > 0))
            {
                if (Line(world, attack, hit) is { } line)
                    log.Info(line);
            }
        }
    }

    private void ObservePlayers(WorldState w)
    {
        var pet = PetWid(w);
        var aiming = w.Players
            .Where(p => p.TargetWid != 0 && (p.TargetWid == w.Host.Wid || p.TargetWid == pet))
            .ToDictionary(p => p.Wid, p => p.TargetWid);
        foreach (var player in w.Players.Where(p => aiming.ContainsKey(p.Wid) && (!_aiming.TryGetValue(p.Wid, out var was) || was != p.TargetWid)))
        {
            var whom = player.TargetWid == w.Host.Wid ? "вас" : "пета";
            log.Info($"Игрок {Describe(player)} держит в цели {whom}, {player.Offset.Direct:0} м");
        }

        _aiming = aiming;
    }

    // Ник и что о нём известно: «ГДЗшница (ур. 6, HP 150/150)»
    private static string Describe(PlayerInfo p)
    {
        var known = new List<string>();
        if (p.Level > 0)
            known.Add($"ур. {p.Level}");
        if (p.MaxHp > 0)
            known.Add($"HP {p.Hp}/{p.MaxHp}");
        return known.Count == 0 ? p.Name : $"{p.Name} ({string.Join(", ", known)})";
    }

    /// <summary>Строка лога; null — удар не наш и не по нам.</summary>
    private static string? Line(WorldState w, AttackEvent attack, AttackHit hit)
    {
        var skill = string.IsNullOrEmpty(attack.SkillName) ? "" : $" ({attack.SkillName})";
        var pet = PetWid(w);
        if (hit.TargetWid == w.Host.Wid)
            return $"{Who(w, attack.AttackerWid)} → вы: {hit.Damage}{skill}, HP {After(w.Host.Hp, hit.Damage)}/{w.Host.MaxHp}";
        if (pet != 0 && hit.TargetWid == pet)
        {
            var cage = w.Pet!.ActiveCage is int c ? w.Pet.InCage(c) : null;
            var hp = cage?.Hp is { } now ? $", HP {After(now, hit.Damage)}/{cage.MaxHp}" : "";
            return $"{Who(w, attack.AttackerWid)} → пет: {hit.Damage}{skill}{hp}";
        }

        if (attack.AttackerWid == w.Host.Wid)
            return $"Вы → {Who(w, hit.TargetWid)}: {hit.Damage}{skill}";
        if (pet != 0 && attack.AttackerWid == pet)
            return $"Пет → {Who(w, hit.TargetWid)}: {hit.Damage}{skill}";
        return null;
    }

    private static uint PetWid(WorldState w) => w.Pet is { IsSummoned: true } p ? p.ActiveWid : 0;

    private static int After(int hp, int damage) => Math.Max(0, hp - damage);

    // Моб или NPC из снимка, игрок — по нику; не в снимке: у игроков WID без старшего бита
    private static string Who(WorldState w, uint wid)
    {
        if (w.Npcs.FirstOrDefault(n => n.Wid == wid) is { } n)
            return n.Name;
        if (w.Players.FirstOrDefault(p => p.Wid == wid) is { } p)
            return $"игрок {p.Name}";
        return (wid & 0x8000_0000) == 0 ? "игрок" : "кто-то";
    }
}

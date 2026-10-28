using System;
using System.Linq;
using BotCH.Core.Logging;

namespace BotCH.Core.World;

/// <summary>
/// Лог урона: удары персонажа и пета по мобам и удары по персонажу и пету — с HP после удара. Смотрит каждый снимок, и когда
/// бот не запущен. Удары без урона (бафы, лечение, промахи) и чужие бои не пишет. HP после удара — HP до удара минус урон:
/// игра снижает HP на 0,3–0,4 с позже, чем удар появляется в списке.
/// </summary>
public sealed class DamageLog(ILogger log)
{
    /// <summary>Источник записей лога: по нему окно показывает или прячет урон.</summary>
    public const string Source = "урон";

    private readonly AttackTracker _tracker = new();

    public void Observe(WorldState world)
    {
        foreach (var attack in _tracker.Update(world))
        {
            foreach (var hit in attack.Hits.Where(h => h.Damage > 0))
            {
                if (Line(world, attack, hit) is { } line)
                    log.Info(line);
            }
        }
    }

    /// <summary>Строка лога; null — удар не наш и не по нам.</summary>
    private static string? Line(WorldState w, AttackEvent attack, AttackHit hit)
    {
        var skill = string.IsNullOrEmpty(attack.SkillName) ? "" : $" ({attack.SkillName})";
        var pet = w.Pet is { IsSummoned: true } p ? p.ActiveWid : 0;
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

    private static int After(int hp, int damage) => Math.Max(0, hp - damage);

    // Моб или NPC из снимка; игроков снимок не читает — у них WID без старшего бита
    private static string Who(WorldState w, uint wid)
        => w.Npcs.FirstOrDefault(n => n.Wid == wid) is { } n ? n.Name : (wid & 0x8000_0000) == 0 ? "игрок" : "кто-то";
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Notes.Core.World;

namespace Notes.Core.Incidents;

/// <summary>
/// Случай текстом — для человека: что случилось и всё, что бот знал о мире в момент каждого события (перс, пет, мобы,
/// игроки, NPC, лут, удары, чат). Время — местное.
/// </summary>
public static class IncidentReport
{
    // Чат в снимке — до 200 сообщений; для разбора хватает последних
    private const int ChatLines = 40;

    public static string Text(Incident incident)
    {
        var text = new StringBuilder();
        text.AppendLine("Что случилось");
        foreach (var e in incident.Events)
            text.AppendLine($"  {Clock(e.Time)}  {e.Text}");
        text.AppendLine($"Записано: {Clock(incident.Start)} — {Clock(incident.End)}, снимков {incident.Snapshots.Count}");

        foreach (var e in incident.Events)
        {
            text.AppendLine();
            text.AppendLine($"=== {Clock(e.Time)}  {e.Text} ===");
            Moment(text, e.World);
        }

        return text.ToString();
    }

    /// <summary>Местное время с миллисекундами: «21:11:37.250».</summary>
    internal static string Clock(DateTime time) => time.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);

    /// <summary>Всё, что известно о мире в снимке.</summary>
    internal static void Moment(StringBuilder text, WorldState w)
    {
        Host(text, w);
        Pet(text, w);

        var mobs = w.Npcs.Where(n => n.Kind == NpcKind.Mob).OrderBy(n => n.Offset.Direct).ToList();
        Section(text, "Мобы", mobs.Select(m => $"{m.Offset,-22} {Mob(w, m)}"));
        Section(text, "Игроки", w.Players.OrderBy(p => p.Offset.Direct).Select(p => $"{p.Offset,-22} {Player(w, p)}"));
        var others = w.Npcs.Where(n => n.Kind != NpcKind.Mob && n.Wid != PetWid(w)).OrderBy(n => n.Offset.Direct);
        Section(text, "NPC и чужие петы", others.Select(n => $"{n.Offset,-22} {n.Name}{Target(w, n.TargetWid, ", цель → ")}"));
        Section(text, "На земле", w.GroundItems.OrderBy(i => i.Offset.Direct).Select(i => $"{i.Offset,-22} {Ground(i)}"));
        Section(text, "Удары сейчас", w.Attacks.SelectMany(a => a.Hits.Select(h => Hit(w, a, h))));

        var chat = w.Chat.Skip(Math.Max(0, w.Chat.Count - ChatLines)).Select(m => $"№{m.Number} [{m.Channel}] {m.Plain}");
        Section(text, w.Chat.Count > ChatLines ? $"Чат (последние {ChatLines} из {w.Chat.Count})" : "Чат", chat);
    }

    private static void Host(StringBuilder text, WorldState w)
    {
        var h = w.Host;
        var parts = new List<string> { $"ур. {h.Level}", $"HP {h.Hp}/{h.MaxHp}", h.MaxMp is { } maxMp ? $"MP {h.Mp}/{maxMp}" : $"MP {h.Mp}" };
        if (h.Flying == true)
            parts.Add("в воздухе");
        if (h.InWater == true)
            parts.Add("в воде");
        if (h.IsCasting)
            parts.Add(h.CastingSkillId is > 0 and var id ? $"кастует {w.Skill(id)?.Name ?? id.ToString(CultureInfo.InvariantCulture)}" : "кастует");
        if (h.Gather is { Active: true })
            parts.Add("копает");
        text.AppendLine($"Персонаж {h.Name}: {string.Join(", ", parts)}, {h.Position}{Target(w, h.TargetWid, ", цель: ")}");
        if (w.InventorySlots > 0)
            text.AppendLine($"Сумка: занято {w.Inventory.Count} из {w.InventorySlots}");
    }

    private static void Pet(StringBuilder text, WorldState w)
    {
        if (w.Pet is not { } pet)
            return;

        if (pet.ActiveCage is not { } cage || pet.InCage(cage) is not { } p)
        {
            text.AppendLine("Пет: не призван");
            return;
        }

        var hp = p.Hp is { } now ? $"HP {now}/{p.MaxHp} ({p.HpPercent} %)" : $"HP {p.HpPercent} %";
        var where = w.Npcs.FirstOrDefault(n => n.Wid == pet.ActiveWid) is { } npc
            ? $", {npc.Offset}{(npc.State.Text() is { } s ? $", {s}" : "")}{Target(w, npc.TargetWid, ", цель → ")}"
            : ", не виден";
        var level = p.Level > 0 ? $" ур. {p.Level}" : "";
        text.AppendLine($"Пет: клетка {cage}, {p.Name ?? "?"}{level}, {hp}{(p.IsHungry ? ", голоден" : "")}{where}");
    }

    private static string Mob(WorldState w, NpcInfo m)
    {
        var parts = new List<string> { m.Name };
        if (m.Level > 0)
            parts.Add($"ур. {m.Level}");
        if (m.State.Text() is { } state)
            parts.Add(state);
        if (m.Hp > 0)
            parts.Add($"HP {m.Hp}");
        if (m.Aggressive == true)
            parts.Add("агр.");
        if (m.Returning)
            parts.Add("возвращается");
        return string.Join(", ", parts) + Target(w, m.TargetWid, ", цель → ");
    }

    private static string Player(WorldState w, PlayerInfo p)
    {
        var parts = new List<string> { p.Name };
        if (PlayerClasses.Name(p.Class) is { } cls)
            parts.Add(cls);
        if (p.Level > 0)
            parts.Add($"ур. {p.Level}");
        if (p.MaxHp > 0)
            parts.Add($"HP {p.Hp}/{p.MaxHp}");
        return string.Join(", ", parts) + Target(w, p.TargetWid, ", цель → ");
    }

    private static string Ground(GroundItem i) => i.Kind switch
    {
        GroundItemKind.Resource => $"{i.Name} (ресурс)",
        GroundItemKind.Money => $"{i.Name} (деньги)",
        _ => i.Name,
    };

    private static string Hit(WorldState w, AttackEvent a, AttackHit h)
    {
        var skill = string.IsNullOrEmpty(a.SkillName) ? "" : $" ({a.SkillName})";
        var damage = h.Damage > 0 ? $": {h.Damage}" : "";
        return $"{Whom(w, a.AttackerWid)} → {Whom(w, h.TargetWid)}{damage}{skill}";
    }

    /// <summary>«, цель → вас» / «пета» / имя; пусто — цели нет.</summary>
    private static string Target(WorldState w, uint wid, string prefix)
        => wid == 0 ? "" : prefix + (wid == w.Host.Wid ? "вас" : wid == PetWid(w) ? "пета" : DamageLog.Who(w, wid));

    private static string Whom(WorldState w, uint wid)
    {
        if (wid == w.Host.Wid)
            return "вы";
        if (wid != 0 && wid == PetWid(w))
            return "пет";
        return DamageLog.Who(w, wid);
    }

    private static uint PetWid(WorldState w) => w.Pet is { IsSummoned: true } p ? p.ActiveWid : 0;

    private static void Section(StringBuilder text, string title, IEnumerable<string> lines)
    {
        var list = lines.ToList();
        if (list.Count == 0)
            return;

        text.AppendLine($"{title} ({list.Count}):");
        foreach (var line in list)
            text.AppendLine("  " + line);
    }
}

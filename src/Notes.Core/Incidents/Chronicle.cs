using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.World;

namespace Notes.Core.Incidents;

/// <summary>
/// Что изменилось от снимка к снимку — строками для хроники случая: кто появился и пропал, кто кого взял в цель, удары,
/// HP, новые сообщения чата. Первый снимок — точка отсчёта: из него строк нет (он целиком в начале хроники).
/// Снимки — по порядку, каждый один раз.
/// </summary>
internal sealed class Chronicle
{
    // Перс в пути сдвигается каждый снимок: место — в хронику, только когда ушёл от прошлой записи на столько, м
    private const float MoveStep = 10f;

    private readonly AttackTracker _attacks = new();
    private WorldState? _previous;
    private Position _reportedAt;

    public IReadOnlyList<string> Changes(WorldState w)
    {
        var fresh = _attacks.Update(w);
        var before = _previous;
        _previous = w;
        if (before is null)
        {
            _reportedAt = w.Host.Position;
            return [];
        }

        var lines = new List<string>();
        Host(lines, before, w);
        Pet(lines, before, w);
        Npcs(lines, before, w);
        Players(lines, before, w);
        Ground(lines, before, w);
        foreach (var attack in fresh)
            lines.AddRange(attack.Hits.Select(h => "удар: " + IncidentReport.Hit(w, attack, h)));
        Chat(lines, before, w);
        return lines;
    }

    private void Host(List<string> lines, WorldState was, WorldState w)
    {
        var (a, b) = (was.Host, w.Host);
        if (a.Hp != b.Hp || a.MaxHp != b.MaxHp)
            lines.Add($"HP перса {a.Hp} → {b.Hp}/{b.MaxHp}");
        if (a.Mp != b.Mp && Math.Abs(a.Mp - b.Mp) * 20 >= (b.MaxMp ?? 0))
            lines.Add($"MP перса {a.Mp} → {b.Mp}");
        if (a.TargetWid != b.TargetWid)
            lines.Add(b.TargetWid == 0 ? "перс снял цель" : $"перс взял в цель {IncidentReport.Target(w, b.TargetWid)}");
        if (a.IsCasting != b.IsCasting || a.CastingSkillId != b.CastingSkillId)
            lines.Add(b.IsCasting ? $"перс {IncidentReport.Casting(w)}" : "перс закончил каст");
        if (a.Flying != b.Flying && b.Flying is { } flying)
            lines.Add(flying ? "перс взлетел" : "перс приземлился");
        if (a.InWater != b.InWater && b.InWater is { } water)
            lines.Add(water ? "перс в воде" : "перс вышел из воды");
        if ((a.Gather?.Active ?? false) != (b.Gather?.Active ?? false))
            lines.Add(b.Gather!.Active ? "перс начал копать" : "перс закончил копать");
        if (b.Position.DistanceTo(_reportedAt) >= MoveStep)
        {
            lines.Add($"перс в {b.Position}");
            _reportedAt = b.Position;
        }
    }

    private static void Pet(List<string> lines, WorldState was, WorldState w)
    {
        var (a, b) = (was.Pet, w.Pet);
        if (a?.ActiveCage != b?.ActiveCage)
            lines.Add(b?.ActiveCage is { } cage ? $"пет призван: клетка {cage}, {b.InCage(cage)?.Name ?? "?"}" : "пет не призван");
        else if (b?.ActiveCage is { } c && a?.InCage(c) is { } before && b.InCage(c) is { } now && before.HpRatio != now.HpRatio)
            lines.Add(now.Hp is { } hp ? $"HP пета {before.Hp} → {hp}/{now.MaxHp}" : $"HP пета {before.HpPercent} → {now.HpPercent} %");
    }

    private static void Npcs(List<string> lines, WorldState was, WorldState w)
    {
        var pet = IncidentReport.PetWid(w);
        var old = was.Npcs.Where(n => n.Wid != pet).ToDictionary(n => n.Wid);
        var now = w.Npcs.Where(n => n.Wid != pet).ToDictionary(n => n.Wid);
        foreach (var n in now.Values)
        {
            var who = n.Kind == NpcKind.Mob ? $"моб {n.Name}" : n.Name;
            if (!old.TryGetValue(n.Wid, out var b))
            {
                lines.Add($"появился {who}, {n.Offset}{(n.IsDead ? ", мёртв" : "")}{IncidentReport.Target(w, n.TargetWid, ", цель → ")}");
                continue;
            }

            if (n.TargetWid != b.TargetWid)
                lines.Add(n.TargetWid == 0 ? $"{who} снял цель" : $"{who} взял в цель {IncidentReport.Target(w, n.TargetWid)}, {n.Offset}");
            if (n.IsDead && !b.IsDead)
                lines.Add($"{who} умер");
            if (n.Hp != b.Hp && n.Hp > 0 && b.Hp > 0)
                lines.Add($"HP {who} {b.Hp} → {n.Hp}");
            if (n.Returning && !b.Returning)
                lines.Add($"{who} бросил цель и возвращается");
        }

        foreach (var b in old.Values.Where(b => !now.ContainsKey(b.Wid)))
            lines.Add($"пропал {(b.Kind == NpcKind.Mob ? $"моб {b.Name}" : b.Name)}, {b.Offset}");
    }

    private static void Players(List<string> lines, WorldState was, WorldState w)
    {
        var old = was.Players.ToDictionary(p => p.Wid);
        var now = w.Players.ToDictionary(p => p.Wid);
        foreach (var p in now.Values)
        {
            if (!old.TryGetValue(p.Wid, out var b))
            {
                lines.Add($"появился игрок {IncidentReport.Player(w, p)}, {p.Offset}");
                continue;
            }

            if (p.TargetWid != b.TargetWid)
                lines.Add(p.TargetWid == 0 ? $"игрок {p.Name} снял цель" : $"игрок {p.Name} взял в цель {IncidentReport.Target(w, p.TargetWid)}, {p.Offset}");
            if (p.Hp != b.Hp && p.MaxHp > 0 && b.MaxHp > 0)
                lines.Add($"HP игрока {p.Name} {b.Hp} → {p.Hp}/{p.MaxHp}");
        }

        // Пропал рядом — ушёл в инвиз или телепорт: расстояние подскажет
        foreach (var b in old.Values.Where(b => !now.ContainsKey(b.Wid)))
            lines.Add($"пропал игрок {b.Name}, {b.Offset}");
    }

    private static void Ground(List<string> lines, WorldState was, WorldState w)
    {
        var old = was.GroundItems.Select(i => i.Id).ToHashSet();
        var now = w.GroundItems.Select(i => i.Id).ToHashSet();
        foreach (var i in w.GroundItems.Where(i => !old.Contains(i.Id)))
            lines.Add($"на земле появилось {IncidentReport.Ground(i)}, {i.Offset}");
        foreach (var i in was.GroundItems.Where(i => !now.Contains(i.Id)))
            lines.Add($"с земли пропало {IncidentReport.Ground(i)}, {i.Offset}");
    }

    private static void Chat(List<string> lines, WorldState was, WorldState w)
    {
        if (w.Chat.Count == 0)
            return;

        var last = was.Chat.Count == 0 ? int.MinValue : was.Chat.Max(m => m.Number);
        // Номера пошли заново (перезаход) — всё, что есть, новое
        if (w.Chat.Max(m => m.Number) < last)
            last = int.MinValue;
        lines.AddRange(w.Chat.Where(m => m.Number > last).Select(m => $"чат [{m.Channel}] {m.Plain}"));
    }
}

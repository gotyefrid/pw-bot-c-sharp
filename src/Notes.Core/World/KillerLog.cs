using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Notes.Core.Logging;

namespace Notes.Core.World;

/// <summary>
/// Кто убил персонажа и пета — по фразам, которые игра пишет в чат. Персонаж: «Itori. Запомните это имя. Так зовут вашего
/// убийцу.» — убийца назван, даже когда удара не видно (удар из инвиза в список ударов не попадает). Пет: «Питомец погиб.» —
/// без убийцы, его берём из последних ударов по пету. Смотрит только новые сообщения: что было в чате до подключения, не пишет.
/// </summary>
public sealed class KillerLog
{
    // Последний удар по пету — не раньше, чем за столько до гибели (в опыте 31.10 — 0,4 с; яд тикает дольше)
    private static readonly TimeSpan PetHitWindow = TimeSpan.FromSeconds(10);

    private readonly ILogger _log;
    private readonly Regex? _killedBy;
    private readonly string _petDied;
    private readonly AttackTracker _tracker = new();
    private readonly List<(DateTime Time, string Line)> _petHits = [];
    private int? _last;

    /// <param name="killedBy">Фраза игры о смерти персонажа, «%s» — убийца (<see cref="Profiles.ChatOffsets.KilledBy"/>); пусто — не пишем.</param>
    /// <param name="petDied">Фраза игры о гибели пета (<see cref="Profiles.ChatOffsets.PetDied"/>); пусто — не пишем.</param>
    public KillerLog(ILogger log, string killedBy, string petDied = "")
    {
        _log = log;
        _petDied = petDied;
        if (killedBy.Contains("%s"))
            _killedBy = new Regex("^" + Regex.Escape(killedBy).Replace("%s", "(.+)") + "$", RegexOptions.Compiled);
    }

    /// <summary>Записали «Вас убил …» или «Пет погиб …»: снимок, в котором заметили, и та же строка, что в логе.</summary>
    public event Action<WorldState, string>? Noted;

    public void Observe(WorldState world)
    {
        if (_killedBy is null && _petDied.Length == 0)
            return;

        RememberPetHits(world);
        if (world.Chat.Count == 0)
            return;

        var newest = world.Chat.Max(m => m.Number);
        // Первый снимок или номера пошли заново (перезаход в игру) — только запоминаем, где мы
        if (_last is not { } last || newest < last)
        {
            _last = newest;
            return;
        }

        foreach (var message in world.Chat.Where(m => m.Number > last))
        {
            var text = message.Plain;
            if (_killedBy?.Match(text) is { Success: true } match)
                Note(world, $"Вас убил {Who(world, match.Groups[1].Value.Trim())}");
            else if (_petDied.Length > 0 && text == _petDied)
                Note(world, PetDeath(world.Time));
        }

        _last = newest;
    }

    private void Note(WorldState world, string line)
    {
        _log.Warning(line);
        Noted?.Invoke(world, line);
    }

    // Удары по пету — пока он призван: после гибели пет уже не в снимке, а фраза в чате приходит позже удара
    private void RememberPetHits(WorldState w)
    {
        var pet = w.Pet is { IsSummoned: true } p ? p.ActiveWid : 0;
        foreach (var attack in _tracker.Update(w))
        {
            foreach (var hit in attack.Hits.Where(h => pet != 0 && h.TargetWid == pet))
            {
                var skill = string.IsNullOrEmpty(attack.SkillName) ? "" : $" ({attack.SkillName})";
                var damage = hit.Damage > 0 ? $": {hit.Damage}" : "";
                _petHits.Add((w.Time, $"{DamageLog.Who(w, attack.AttackerWid)}{damage}{skill}"));
            }
        }

        _petHits.RemoveAll(h => w.Time - h.Time > PetHitWindow);
    }

    private string PetDeath(DateTime now)
    {
        var last = _petHits.LastOrDefault(h => now - h.Time <= PetHitWindow);
        _petHits.Clear();
        return last.Line is null
            ? "Пет погиб, удара не видно (яд или невидимый игрок?)"
            : $"Пет погиб: последний удар — {last.Line}, за {(now - last.Time).TotalSeconds:0.0} с до гибели";
    }

    // Игрок рядом — с классом и уровнем, если игра их знает; моб и остальные — как в чате
    private static string Who(WorldState w, string name)
    {
        if (w.Players.FirstOrDefault(p => p.Name == name) is not { } player)
            return name;

        var known = new[] { PlayerClasses.Name(player.Class), player.Level > 0 ? $"ур. {player.Level}" : null }.Where(s => s is not null).ToList();
        return known.Count == 0 ? $"игрок {name}" : $"игрок {name} ({string.Join(", ", known)})";
    }
}

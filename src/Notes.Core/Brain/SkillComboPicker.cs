using System;
using System.Collections.Generic;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Какую строку связки скиллов жать сейчас: строки сверху вниз, первая подходящая — включена, условия выполнены, правило
/// повтора разрешает, скилл изучен и готов.
/// Строки «ждать» (<see cref="ComboRow.WaitReady"/>) — очередь по кругу: из них в ход идёт только одна — следующая после
/// нажатой (с начала боя — верхняя); не выполнены её условия — следующая за ней. Она на перезарядке — поиск
/// останавливается, ждём её. Так «ждать» на всех строках — строго 1 → 2 → 3 → 1. Строки без «ждать» — как обычно, по месту.
/// Помнит, что жали, — для «раз за бой», «не чаще N с», «последним был» и очереди.
/// </summary>
public sealed class SkillComboPicker
{
    // Когда жали строку: ключ — номер строки и скилл, чтобы после перестановки строк не путать
    private readonly Dictionary<(int Row, int Skill), DateTime> _pressed = [];
    private readonly HashSet<(int Row, int Skill)> _thisFight = [];

    // Очередь строк «ждать»: с какой строки искать следующую (после нажатой); с начала боя — сверху
    private int _queueFrom;

    /// <summary>Что связка нажала последним в этом бою; 0 — ничего.</summary>
    public int LastSkill { get; private set; }

    /// <summary>Новый моб: «раз за бой» и «последним был» — заново. «Не чаще N с» помнится и между боями.</summary>
    public void NewFight()
    {
        _thisFight.Clear();
        LastSkill = 0;
        _queueFrom = 0;
    }

    public ComboChoice Pick(IReadOnlyList<ComboRow> rows, WorldState world, NpcInfo mob, TimeSpan fightTime, DateTime now)
    {
        var skipped = new List<string>();
        var queue = QueueRow(rows, world, mob, fightTime, now);
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!row.Enabled)
                continue;

            if (row.WaitReady && i != queue)
            {
                if (queue >= 0)
                    skipped.Add($"{row.Title} — очередь у {rows[queue].Title}");
                else if (WhyNotNow(row, i, world, mob, fightTime, now) is { } blocked)
                    skipped.Add($"{row.Title} — {blocked}");
                continue;
            }

            if (WhyNotNow(row, i, world, mob, fightTime, now) is { } why)
            {
                skipped.Add($"{row.Title} — {why}");
                continue;
            }

            switch (world.Skill(row.SkillId))
            {
                case null:
                    skipped.Add($"{row.Title} — не изучен");
                    continue;
                case { IsReady: false } skill when row.WaitReady:
                    return new ComboChoice(null, $"жду {row.Title}: перезарядка {skill.CooldownLeftMs / 1000.0:0.0} с", skipped);
                case { IsReady: false } skill:
                    skipped.Add($"{row.Title} — перезарядка {skill.CooldownLeftMs / 1000.0:0.0} с");
                    continue;
                default:
                    return new ComboChoice(row, null, skipped) { Index = i };
            }
        }

        return new ComboChoice(null, null, skipped);
    }

    /// <summary>Строку нажали (действие ушло в игру).</summary>
    public void Pressed(ComboChoice choice, DateTime now)
    {
        if (choice.Row is not { } row)
            return;
        var key = (choice.Index, row.SkillId);
        _pressed[key] = now;
        _thisFight.Add(key);
        LastSkill = row.SkillId;
        if (row.WaitReady)
            _queueFrom = choice.Index + 1;
    }

    // Чья очередь среди строк «ждать»: первая по кругу от _queueFrom, у которой выполнены условия и повтор и скилл изучен.
    // Перезарядка не в счёт — её как раз и ждём. -1 — никого
    private int QueueRow(IReadOnlyList<ComboRow> rows, WorldState world, NpcInfo mob, TimeSpan fightTime, DateTime now)
    {
        for (var step = 0; step < rows.Count; step++)
        {
            var i = (_queueFrom + step) % rows.Count;
            var row = rows[i];
            if (row.Enabled && row.WaitReady && world.Skill(row.SkillId) is not null
                && WhyNotNow(row, i, world, mob, fightTime, now) is null)
                return i;
        }
        return -1;
    }

    // Правило повтора и условия; null — можно жать
    private string? WhyNotNow(ComboRow row, int index, WorldState world, NpcInfo mob, TimeSpan fightTime, DateTime now)
    {
        var key = (index, row.SkillId);
        switch (row.Repeat)
        {
            case ComboRepeat.OncePerFight when _thisFight.Contains(key):
                return "уже был в этом бою";
            case ComboRepeat.Interval when _pressed.TryGetValue(key, out var last) && now - last < TimeSpan.FromSeconds(row.IntervalSeconds):
                return $"не чаще раза в {row.IntervalSeconds} с";
        }

        foreach (var condition in row.Conditions)
            if (Fails(condition, world, mob, fightTime) is { } why)
                return why;
        return null;
    }

    // Почему условие не выполнено; null — выполнено. Значение неизвестно (поле не найдено для сервера) — не выполнено
    private string? Fails(ComboCondition condition, WorldState world, NpcInfo mob, TimeSpan fightTime)
    {
        var host = world.Host;
        float? value = condition.Check switch
        {
            ComboCheck.MobHp => mob.MaxHp > 0 ? mob.Hp * 100f / mob.MaxHp : null,
            ComboCheck.MyHp => host.MaxHp > 0 ? host.Hp * 100f / host.MaxHp : null,
            ComboCheck.MyMp => host.MaxMp > 0 ? host.Mp * 100f / host.MaxMp.Value : null,
            ComboCheck.Distance => mob.Offset.Direct,
            ComboCheck.FightSeconds => (float)fightTime.TotalSeconds,
            _ => null,
        };

        var met = condition.Check switch
        {
            ComboCheck.SkillReady => world.Skill(condition.SkillId) is { IsReady: true },
            ComboCheck.SkillCooling => world.Skill(condition.SkillId) is { IsReady: false },
            ComboCheck.LastSkill => LastSkill == condition.SkillId,
            _ => value is { } v && (condition.Below ? v < condition.Value : v > condition.Value),
        };
        if (met)
            return null;
        if (condition.AboutSkill)
            return $"не выполнено «{condition.Text}»";
        return value is { } now ? $"не выполнено «{condition.Text}» (сейчас {now:0})" : $"неизвестно для «{condition.Text}»";
    }
}

/// <summary>
/// Выбор связки: <see cref="Row"/> — что жать (null — нечего); <see cref="Waiting"/> — строка «ждать» на перезарядке;
/// <see cref="Skipped"/> — какие строки пропущены и почему (для лога).
/// </summary>
public sealed record ComboChoice(ComboRow? Row, string? Waiting, IReadOnlyList<string> Skipped)
{
    /// <summary>Номер выбранной строки (с 0).</summary>
    public int Index { get; init; } = -1;
}

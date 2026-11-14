using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.World;

namespace Notes.Core.Settings;

/// <summary>
/// Связка скиллов в окне без самого окна: как строка читается в таблице, какие проверки можно выбрать, условие по умолчанию,
/// копия строки для окна строки, перестановка строк и из каких скиллов выбирать.
/// </summary>
public static class ComboEditing
{
    /// <summary>Что можно проверять — в порядке списка в окне.</summary>
    public static IReadOnlyList<ComboCheck> Checks { get; } =
    [
        ComboCheck.MobHp, ComboCheck.MyHp, ComboCheck.MyMp, ComboCheck.Distance, ComboCheck.FightSeconds,
        ComboCheck.SkillReady, ComboCheck.SkillCooling, ComboCheck.LastSkill,
    ];

    /// <summary>Проверка в списке окна: «HP моба, %», «скилл готов».</summary>
    public static string CheckTitle(ComboCheck check) => check switch
    {
        ComboCheck.MobHp => "HP моба, %",
        ComboCheck.MyHp => "моё HP, %",
        ComboCheck.MyMp => "моя MP, %",
        ComboCheck.Distance => "до моба, м",
        ComboCheck.FightSeconds => "бой идёт, с",
        ComboCheck.SkillReady => "скилл готов",
        ComboCheck.SkillCooling => "скилл на перезарядке",
        ComboCheck.LastSkill => "последним был скилл",
        _ => check.ToString(),
    };

    /// <summary>Условия строки одной строкой: «HP моба &lt; 30% и готов «Молния»»; условий нет — «всегда».</summary>
    public static string ConditionsText(ComboRow row)
        => row.Conditions.Count == 0 ? "всегда" : string.Join(" и ", row.Conditions.Select(c => c.Text));

    /// <summary>Как часто строку можно жать: «каждый раз», «1 раз за бой», «не чаще 15 с».</summary>
    public static string RepeatText(ComboRow row) => RepeatText(row.Repeat, row.IntervalSeconds);

    public static string RepeatText(ComboRepeat repeat, int seconds) => repeat switch
    {
        ComboRepeat.OncePerFight => "1 раз за бой",
        ComboRepeat.Interval => $"не чаще {seconds} с",
        _ => "каждый раз",
    };

    /// <summary>Новое условие этой проверки — с понятным числом для начала.</summary>
    public static ComboCondition NewCondition(ComboCheck check, int skillId = 0, string skillName = "") => check switch
    {
        ComboCheck.MobHp => new ComboCondition { Check = check, Below = true, Value = 30 },
        ComboCheck.MyHp => new ComboCondition { Check = check, Below = true, Value = 50 },
        ComboCheck.MyMp => new ComboCondition { Check = check, Below = false, Value = 30 },
        ComboCheck.Distance => new ComboCondition { Check = check, Below = true, Value = 5 },
        ComboCheck.FightSeconds => new ComboCondition { Check = check, Below = false, Value = 10 },
        _ => new ComboCondition { Check = check, SkillId = skillId, SkillName = skillName },
    };

    /// <summary>
    /// Поменять, что проверяет условие. Число и скилл не теряются, если новая проверка такого же рода (про число или про скилл);
    /// иначе — как у нового условия.
    /// </summary>
    public static void ChangeCheck(ComboCondition condition, ComboCheck check)
    {
        if (condition.Check == check)
            return;

        var wasSkill = condition.AboutSkill;
        condition.Check = check;
        if (wasSkill == condition.AboutSkill)
            return;

        var fresh = NewCondition(check, condition.SkillId, condition.SkillName);
        condition.Below = fresh.Below;
        condition.Value = fresh.Value;
    }

    /// <summary>Копия строки для окна строки: правки в ней не трогают строку, пока не нажали «Сохранить».</summary>
    public static ComboRow Copy(ComboRow row)
    {
        var copy = new ComboRow { Enabled = row.Enabled };
        Apply(copy, row);
        return copy;
    }

    /// <summary>
    /// Настройки из окна строки — в строку: скилл, условия (копиями), «ждать», как часто. «Вкл» не трогает: она правится в
    /// таблице, а не в окне строки.
    /// </summary>
    public static void Apply(ComboRow target, ComboRow edited)
    {
        target.SkillId = edited.SkillId;
        target.SkillName = edited.SkillName;
        target.Conditions = edited.Conditions.Select(c => new ComboCondition
        {
            Check = c.Check, Below = c.Below, Value = c.Value, SkillId = c.SkillId, SkillName = c.SkillName,
        }).ToList();
        target.WaitReady = edited.WaitReady;
        target.Repeat = edited.Repeat;
        target.IntervalSeconds = edited.IntervalSeconds;
    }

    /// <summary>Сдвинуть элемент на <paramref name="step"/> (−1 — выше, +1 — ниже). Новое место; сдвинуть некуда — −1.</summary>
    public static int Move<T>(IList<T> items, int at, int step)
    {
        var to = at + step;
        if (at < 0 || at >= items.Count || to < 0 || to >= items.Count || step == 0)
            return -1;

        var item = items[at];
        items.RemoveAt(at);
        items.Insert(to, item);
        return to;
    }

    /// <summary>
    /// Из каких скиллов выбирать. Персонаж в игре (<paramref name="learned"/> не null) — изученные (кроме не подходящих
    /// <paramref name="include"/>), а скиллы из строк, которых у него нет, — в конце с пометкой «не изучен». Не в игре — только
    /// скиллы из строк по запомненным названиям: изучены ли, неизвестно.
    /// </summary>
    public static List<ComboSkill> Skills(IEnumerable<SkillInfo>? learned, IEnumerable<ComboRow> rows, Func<int, bool>? include = null)
    {
        // Запомненные в строках и условиях названия: подставить, пока игра не дала своё
        var remembered = new Dictionary<int, string>();
        foreach (var row in rows)
        {
            Remember(remembered, row.SkillId, row.SkillName);
            foreach (var condition in row.Conditions.Where(c => c.AboutSkill))
                Remember(remembered, condition.SkillId, condition.SkillName);
        }

        if (learned is null)
            return remembered.Select(p => new ComboSkill(p.Key, NameOf(p.Key, p.Value), false)).OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var skills = learned.Where(s => s.Id > 0 && (include?.Invoke(s.Id) ?? true))
            .GroupBy(s => s.Id).Select(g => g.First())
            .Select(s => new ComboSkill(s.Id, NameOf(s.Id, s.Name ?? (remembered.TryGetValue(s.Id, out var name) ? name : null)), false))
            .ToList();
        var known = new HashSet<int>(learned.Select(s => s.Id));
        skills.AddRange(remembered.Where(p => !known.Contains(p.Key)).Select(p => new ComboSkill(p.Key, NameOf(p.Key, p.Value), true)));
        return skills;
    }

    private static void Remember(Dictionary<int, string> names, int id, string name)
    {
        if (id <= 0)
            return;
        if (!names.TryGetValue(id, out var before) || before.Length == 0)
            names[id] = name?.Trim() ?? "";
    }

    private static string NameOf(int id, string? name) => string.IsNullOrWhiteSpace(name) ? $"скилл {id}" : name!.Trim();
}

/// <summary>Скилл в списке связки: <paramref name="Missing"/> — персонаж в игре, а этого скилла у него нет.</summary>
public sealed record ComboSkill(int Id, string Name, bool Missing);

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Notes.Core.Settings;

/// <summary>
/// Строка связки скиллов: какой скилл, при каких условиях жать и как часто. Бот смотрит строки сверху вниз и жмёт первую
/// подходящую (<see cref="Brain.SkillComboPicker"/>).
/// </summary>
public sealed class ComboRow
{
    public int SkillId { get; set; }

    /// <summary>Название скилла, запомненное при выборе: для окна и лога, когда перс не в игре или скилла у него нет.</summary>
    public string SkillName { get; set; } = "";

    /// <summary>Выключенная строка пропускается, как будто её нет.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Все условия сразу; пусто — всегда.</summary>
    public List<ComboCondition> Conditions { get; set; } = [];

    /// <summary>
    /// Условия выполнены, а скилл на перезарядке: false — строку пропустить, смотреть ниже; true — ждать этот скилл, ниже не
    /// смотреть (жёсткая очередь «строго 1 → 2 → 3»).
    /// </summary>
    public bool WaitReady { get; set; }

    public ComboRepeat Repeat { get; set; } = ComboRepeat.Always;

    /// <summary>Для <see cref="ComboRepeat.Interval"/>: не чаще раза в столько секунд.</summary>
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>«Огненный шар» — по запомненному названию; названия нет — «скилл 123».</summary>
    [JsonIgnore]
    public string Title => SkillName.Length > 0 ? $"«{SkillName}»" : $"скилл {SkillId}";

    /// <summary>Без пустых строк и условий, значения — в пределах.</summary>
    public static List<ComboRow> Clean(IEnumerable<ComboRow?>? rows)
        => (rows ?? [])
            .Where(r => r is not null && r.SkillId > 0)
            .Select(r =>
            {
                r!.SkillName = r.SkillName?.Trim() ?? "";
                r.Conditions = (r.Conditions ?? []).Where(c => c is not null).Select(c => c.Clean()).ToList();
                r.IntervalSeconds = Math.Min(3600, Math.Max(1, r.IntervalSeconds));
                return r;
            })
            .ToList();
}

/// <summary>Как часто можно жать строку.</summary>
public enum ComboRepeat
{
    /// <summary>Каждый раз, как дойдёт очередь.</summary>
    Always,

    /// <summary>Один раз за бой с мобом.</summary>
    OncePerFight,

    /// <summary>Не чаще раза в <see cref="ComboRow.IntervalSeconds"/> секунд (и между боями).</summary>
    Interval,
}

/// <summary>Условие строки связки: что проверяем, больше или меньше, с чем сравниваем.</summary>
public sealed class ComboCondition
{
    public ComboCheck Check { get; set; }

    /// <summary>Для числовых проверок: true — меньше <see cref="Value"/>, false — больше.</summary>
    public bool Below { get; set; }

    /// <summary>Проценты (HP, MP), метры (дистанция) или секунды (время боя).</summary>
    public int Value { get; set; }

    /// <summary>Для проверок про скилл (<see cref="ComboCheck.SkillReady"/> и др.): какой скилл.</summary>
    public int SkillId { get; set; }

    /// <summary>Название скилла <see cref="SkillId"/>, запомненное при выборе.</summary>
    public string SkillName { get; set; } = "";

    /// <summary>Проверка про скилл, а не число.</summary>
    [JsonIgnore]
    public bool AboutSkill => Check is ComboCheck.SkillReady or ComboCheck.SkillCooling or ComboCheck.LastSkill;

    /// <summary>Как условие читается в окне и логе: «HP моба &lt; 30%», «готов «Молния»».</summary>
    [JsonIgnore]
    public string Text
    {
        get
        {
            var skill = SkillName.Length > 0 ? $"«{SkillName}»" : $"скилл {SkillId}";
            var op = Below ? "<" : ">";
            return Check switch
            {
                ComboCheck.MobHp => $"HP моба {op} {Value}%",
                ComboCheck.MyHp => $"моё HP {op} {Value}%",
                ComboCheck.MyMp => $"моя MP {op} {Value}%",
                ComboCheck.Distance => $"до моба {op} {Value} м",
                ComboCheck.FightSeconds => $"бой идёт {op} {Value} с",
                ComboCheck.SkillReady => $"готов {skill}",
                ComboCheck.SkillCooling => $"на перезарядке {skill}",
                ComboCheck.LastSkill => $"последним был {skill}",
                _ => Check.ToString(),
            };
        }
    }

    public ComboCondition Clean()
    {
        Value = Math.Min(100000, Math.Max(0, Value));
        SkillName = SkillName?.Trim() ?? "";
        return this;
    }
}

/// <summary>Что проверяет условие связки. Только то, что бот уже точно читает из игры.</summary>
public enum ComboCheck
{
    /// <summary>HP цели, %.</summary>
    MobHp,

    /// <summary>Своё HP, %.</summary>
    MyHp,

    /// <summary>Своя мана, %.</summary>
    MyMp,

    /// <summary>Расстояние до цели, м.</summary>
    Distance,

    /// <summary>Сколько секунд идёт бой с этим мобом.</summary>
    FightSeconds,

    /// <summary>Скилл <see cref="ComboCondition.SkillId"/> готов (не на перезарядке).</summary>
    SkillReady,

    /// <summary>Скилл <see cref="ComboCondition.SkillId"/> на перезарядке.</summary>
    SkillCooling,

    /// <summary>Последним в этом бою связка нажала скилл <see cref="ComboCondition.SkillId"/>.</summary>
    LastSkill,
}

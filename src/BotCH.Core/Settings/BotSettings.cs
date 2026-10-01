using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.World;

namespace BotCH.Core.Settings;

/// <summary>
/// Все настройки бота. Значения по умолчанию — как в старом боте (BotForm + ini).
/// Окно меняет «свой» экземпляр, бот получает копию (<see cref="Clone"/>) — правки в окне не попадают в бота посреди решения.
/// </summary>
public sealed class BotSettings
{
    public ConnectionSettings Connection { get; set; } = new();
    public TargetSettings Target { get; set; } = new();
    public CombatSettings Combat { get; set; } = new();
    public LootSettings Loot { get; set; } = new();
    public PotionSettings Potions { get; set; } = new();
    public PetSettings Pet { get; set; } = new();

    public BotSettings Clone() => SettingsJson.Parse(SettingsJson.Serialize(this));

    /// <summary>Приводит значения к допустимым (после ручной правки файла или старой версии).</summary>
    public BotSettings Normalize()
    {
        Connection ??= new();
        Target ??= new();
        Combat ??= new();
        Loot ??= new();
        Potions ??= new();
        Pet ??= new();

        Target.MobNames = MobNameFilter.Clean(Target.MobNames);
        Loot.ItemNames = MobNameFilter.Clean(Loot.ItemNames);
        Target.MobTimeoutSeconds = Clamp(Target.MobTimeoutSeconds, 10, 3600);
        Combat.ComeCloserDistance = Clamp(Combat.ComeCloserDistance, 1, 30);
        Loot.Attempts = Clamp(Loot.Attempts, 1, 20);
        Potions.HpPercent = Clamp(Potions.HpPercent, 0, 100);
        Potions.MpBelow = Math.Max(0, Potions.MpBelow);
        Pet.Cage = Clamp(Pet.Cage, 1, 10);
        Pet.HealPercent = Clamp(Pet.HealPercent, 0, 100);
        return this;
    }

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
    private static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));
}

public sealed class ConnectionSettings
{
    /// <summary>Профиль сервера (файл в Profiles).</summary>
    public string ServerId { get; set; } = "pwclassic136";

    /// <summary>Переименовывать окна клиентов в ник персонажа.</summary>
    public bool RenameWindows { get; set; } = true;

    /// <summary>Не давать клиенту «засыпать» без фокуса (запись в данные игры раз в секунду).</summary>
    public bool Unfreeze { get; set; }
}

public sealed class TargetSettings
{
    /// <summary>Нападать на мобов (старое «Kill Mobs»). Выключено — бот только лечится/кормит пета.</summary>
    public bool KillMobs { get; set; } = true;

    /// <summary>Сначала бить моба, который бьёт перса или пета (старое «Find agr mob»).</summary>
    public bool PreferAggressive { get; set; } = true;

    /// <summary>Нападать только на мобов из списка названий (старое «Check ID»).</summary>
    public bool UseMobList { get; set; }

    /// <summary>Названия мобов. Одно название — много мобов с разными WID.</summary>
    public List<string> MobNames { get; set; } = [];

    /// <summary>Сколько секунд биться с одним мобом, прежде чем бросить.</summary>
    public int MobTimeoutSeconds { get; set; } = 120;
}

public sealed class CombatSettings
{
    public bool UseSkill { get; set; }

    /// <summary>ID атакующего скилла. 299 — по умолчанию в старом боте.</summary>
    public int AttackSkillId { get; set; } = 299;

    /// <summary>Обычная атака (раз в ~5 с).</summary>
    public bool UseSword { get; set; }

    /// <summary>Подходить к мобу на <see cref="ComeCloserDistance"/>, если обычная атака выключена.</summary>
    public bool ComeCloser { get; set; }

    public float ComeCloserDistance { get; set; } = 8;
}

public sealed class LootSettings
{
    public bool Enabled { get; set; }

    /// <summary>Сколько раз подбирать после смерти моба.</summary>
    public int Attempts { get; set; } = 4;

    public bool PickMoney { get; set; } = true;
    public bool PickItems { get; set; } = true;

    /// <summary>Как использовать <see cref="ItemNames"/>: не использовать / только они / все, кроме них.</summary>
    public LootListMode ListMode { get; set; } = LootListMode.All;

    /// <summary>Названия предметов на земле (как в игре: «Мягкий мех»).</summary>
    public List<string> ItemNames { get; set; } = [];
}

public enum LootListMode
{
    /// <summary>Подбирать всё (список не используется).</summary>
    All,
    /// <summary>Белый список: только предметы из списка.</summary>
    OnlyListed,
    /// <summary>Чёрный список: всё, кроме предметов из списка.</summary>
    ExceptListed,
}

public sealed class PotionSettings
{
    /// <summary>Пить банку HP, когда HP ниже этого процента.</summary>
    public int HpPercent { get; set; } = 80;

    /// <summary>Пить банку MP, когда MP ниже этого числа (абсолютные единицы, как в старом боте).</summary>
    public int MpBelow { get; set; } = 100;
}

public sealed class PetSettings
{
    /// <summary>Пользоваться петом. Выключено или пета нет (не друид) — всё про пета пропускается.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Клетка 1..10.</summary>
    public int Cage { get; set; } = 1;

    /// <summary>Лечить пета, когда его HP ниже этого процента.</summary>
    public int HealPercent { get; set; } = 70;
}

/// <summary>Подбирать ли предмет с земли — единственное место, где это решается.</summary>
public static class LootFilter
{
    public static bool Allows(LootSettings loot, GroundItemKind kind, string? name)
    {
        // Ресурс копается, а не подбирается — никогда
        if (kind == GroundItemKind.Resource)
            return false;
        if (kind == GroundItemKind.Money && !loot.PickMoney)
            return false;
        if (kind == GroundItemKind.Item && !loot.PickItems)
            return false;

        return loot.ListMode switch
        {
            LootListMode.OnlyListed => MobNameFilter.Contains(loot.ItemNames, name),
            LootListMode.ExceptListed => !MobNameFilter.Contains(loot.ItemNames, name),
            _ => true,
        };
    }

    public static bool Allows(LootSettings loot, GroundItem item) => Allows(loot, item.Kind, item.Name);
}

/// <summary>Сравнение названий (мобов, предметов): без учёта регистра и пробелов по краям.</summary>
public static class MobNameFilter
{
    public static List<string> Clean(IEnumerable<string>? names)
        => (names ?? [])
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Можно ли нападать на моба с таким названием при этих настройках.</summary>
    public static bool Allows(TargetSettings target, string? mobName)
    {
        if (!target.UseMobList || target.MobNames.Count == 0)
            return true;

        return Contains(target.MobNames, mobName);
    }

    public static bool Contains(IEnumerable<string> names, string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length > 0 && names.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

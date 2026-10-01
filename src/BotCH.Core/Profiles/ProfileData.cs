using System.Collections.Generic;

namespace BotCH.Core.Profiles;

// Данные профиля сервера — 1:1 с JSON-файлом в Profiles/*.jsonc.
// Все смещения — uint, в JSON пишутся строкой "0x...". Смещение 0 = «на этом сервере нет/не найдено».
// Пояснения «как нашли» — комментариями в самом JSON, здесь только краткий смысл полей.

public sealed class ProfileData
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClientProcessName { get; init; } = "elementclient";

    public BaseOffsets Base { get; init; } = new();
    public HostOffsets Host { get; init; } = new();
    public PetManagerOffsets PetManager { get; init; } = new();
    public PetOffsets Pet { get; init; } = new();
    public WorldOffsets World { get; init; } = new();
    public NpcOffsets Npc { get; init; } = new();
    public GroundItemOffsets GroundItem { get; init; } = new();
    public InventoryOffsets Inventory { get; init; } = new();
    public PotionOffsets Potion { get; init; } = new();
    public PetFoodOffsets PetFood { get; init; } = new();
    public SkillOffsets Skill { get; init; } = new();
    public ClassSkills Skills { get; init; } = new();

    /// <summary>Функции клиента для прямого вызова, ключи — <see cref="GameFunctions"/>.</summary>
    public Dictionary<string, GameFunction> Functions { get; init; } = new();

    /// <summary>
    /// Функции, которые нельзя вызывать никогда (rva): выход из игры, отпустить пета.
    /// Вызов по такому адресу блокируется, даже если он по ошибке окажется в <see cref="Functions"/>.
    /// </summary>
    public Dictionary<string, uint> ForbiddenFunctions { get; init; } = new();
}

public sealed class BaseOffsets
{
    /// <summary>Статический указатель от начала модуля: [модуль + BasePointer] = база.</summary>
    public uint BasePointer { get; init; }
    /// <summary>[база + Game] = game.</summary>
    public uint Game { get; init; }
    /// <summary>[база + Unfreeze] — сюда пишется 1 (unfreeze).</summary>
    public uint Unfreeze { get; init; }
}

/// <summary>Персонаж (CECHostPlayer): [game + Struct].</summary>
public sealed class HostOffsets
{
    public uint Struct { get; init; }
    /// <summary>Указатель на ник (UTF-16).</summary>
    public uint NamePointer { get; init; }
    /// <summary>Байт «персонаж кастует».</summary>
    public uint CastFlag { get; init; }
    public uint Wid { get; init; }
    public uint Level { get; init; }
    public uint Hp { get; init; }
    public uint Mp { get; init; }
    public uint MaxHp { get; init; }
    public uint MaxMp { get; init; }
    /// <summary>WID текущей цели.</summary>
    public uint TargetId { get; init; }
    /// <summary>Перезарядка корма пета, мс осталось (0 — можно кормить).</summary>
    public uint PetFoodCooldown { get; init; }
    /// <summary>Координаты: X, высота, Y — три float подряд.</summary>
    public uint Location { get; init; }
    /// <summary>[перс + WorkMan] — менеджер «работ» (CECHPWorkMan).</summary>
    public uint WorkMan { get; init; }
    /// <summary>[перс + Inventory] — сумка.</summary>
    public uint Inventory { get; init; }
    /// <summary>[перс + Skills] — массив указателей на скиллы.</summary>
    public uint Skills { get; init; }
    public uint SkillsCount { get; init; }
    /// <summary>[перс + PetManager] — менеджер петов.</summary>
    public uint PetManager { get; init; }
}

public sealed class PetManagerOffsets
{
    /// <summary>Номер призванной клетки (с 0), -1 — пета нет.</summary>
    public uint ActiveCage { get; init; }
    /// <summary>Массив указателей на петов по клеткам: [менеджер + Cages + (клетка-1)*4].</summary>
    public uint Cages { get; init; }
    public int CageCount { get; init; }
    /// <summary>WID призванного пета (в списке мобов он с типом 9).</summary>
    public uint ActivePetWid { get; init; }
}

/// <summary>Пет в клетке.</summary>
public sealed class PetOffsets
{
    /// <summary>HP в долях (float 0..1).</summary>
    public uint HpRatio { get; init; }
    /// <summary>Сытость.</summary>
    public uint Hunger { get; init; }
}

public sealed class WorldOffsets
{
    /// <summary>[game + World] — мир.</summary>
    public uint World { get; init; }
    /// <summary>[мир + Npcs] — менеджер мобов/NPC/петов.</summary>
    public uint Npcs { get; init; }
    /// <summary>[мир + GroundItems] — менеджер предметов на земле.</summary>
    public uint GroundItems { get; init; }
    /// <summary>[менеджер + SlotArray] — массив ячеек.</summary>
    public uint SlotArray { get; init; }
    public int SlotCount { get; init; }
    /// <summary>[менеджер + Count] — сколько объектов в списке (для самопроверки).</summary>
    public uint Count { get; init; }
    /// <summary>[ячейка + ObjectInSlot] — объект.</summary>
    public uint ObjectInSlot { get; init; }
}

/// <summary>Моб/NPC/пет в мире.</summary>
public sealed class NpcOffsets
{
    public uint Wid { get; init; }
    /// <summary>6 моб, 7 NPC, 9 пет.</summary>
    public uint Type { get; init; }
    /// <summary>1 стоит, 2 физ. атака, 3 каст, 4 мёртв, 5 идёт.</summary>
    public uint State { get; init; }
    /// <summary>Уровень моба (0 — поле не найдено для этого сервера).</summary>
    public uint Level { get; init; }
    /// <summary>Только у выбранной цели.</summary>
    public uint Hp { get; init; }
    public uint Distance { get; init; }
    /// <summary>WID того, кого бьёт моб; 0 — нейтрален.</summary>
    public uint Target { get; init; }
    public uint NamePointer { get; init; }
    public uint Location { get; init; }
}

public sealed class GroundItemOffsets
{
    public uint Id { get; init; }
    public uint Tid { get; init; }
    /// <summary>1 предмет, 2 ресурс, 3 монеты.</summary>
    public uint Kind { get; init; }
    public uint Distance { get; init; }
    public uint NamePointer { get; init; }
    public uint Location { get; init; }
}

public sealed class InventoryOffsets
{
    public uint Items { get; init; }
    public uint Size { get; init; }
    public uint ItemCategory { get; init; }
    public uint ItemTid { get; init; }
    public uint ItemCount { get; init; }
    /// <summary>Описание (шаблон) банки и большинства предметов.</summary>
    public uint ItemEssence { get; init; }
    /// <summary>Описание корма пета (у него другой класс предмета).</summary>
    public uint FoodEssence { get; init; }
    public int CategoryPotion { get; init; }
    public int CategoryPetFood { get; init; }
}

/// <summary>Описание банки.</summary>
public sealed class PotionOffsets
{
    public uint RequiredLevel { get; init; }
    public uint Hp { get; init; }
    public uint HpSeconds { get; init; }
    public uint Mp { get; init; }
    public uint MpSeconds { get; init; }
}

/// <summary>Описание корма.</summary>
public sealed class PetFoodOffsets
{
    public uint Loyalty { get; init; }
}

/// <summary>Объект скилла.</summary>
public sealed class SkillOffsets
{
    public uint Id { get; init; }
    public uint Level { get; init; }
    /// <summary>Сколько мс перезарядки осталось (0 — готов).</summary>
    public uint CooldownLeft { get; init; }
    public uint CooldownFull { get; init; }
    /// <summary>Младший бит — «на перезарядке».</summary>
    public uint Flags { get; init; }
}

/// <summary>ID скиллов класса (пока друид).</summary>
public sealed class ClassSkills
{
    public int HealPet { get; init; }
    public int RevivePet { get; init; }
    public int DefaultAttack { get; init; }
    public List<int> NotAttack { get; init; } = new();
}

public enum CallingConvention
{
    /// <summary>Пакеты серверу c2s_SendCmd*: аргументы в стеке, стек чистит вызывающий.</summary>
    Cdecl,
    /// <summary>Методы объектов клиента: this в ecx.</summary>
    Thiscall,
}

public sealed class GameFunction
{
    /// <summary>Смещение от начала модуля (адрес в IDA/x32dbg = 0x400000 + Rva).</summary>
    public uint Rva { get; init; }
    /// <summary>Начало функции. Не совпало по адресу — ищем по всему коду; не нашли — не вызываем.</summary>
    public Signature? Signature { get; init; }
    public CallingConvention Convention { get; init; }
}

/// <summary>Имена функций в профиле.</summary>
public static class GameFunctions
{
    public const string SelectTarget = "selectTarget";
    public const string Unselect = "unselect";
    public const string NormalAttack = "normalAttack";
    public const string Pickup = "pickup";
    public const string UseItem = "useItem";
    public const string CastSkill = "castSkill";
    public const string SummonPet = "summonPet";
    public const string PetCtrl = "petCtrl";
    public const string HostApplySkill = "hostApplySkill";
    public const string HostPickupObject = "hostPickupObject";
    public const string WorkCreate = "workCreate";
    public const string WorkMoveSetDestination = "workMoveSetDestination";
    public const string WorkStart = "workStart";
}

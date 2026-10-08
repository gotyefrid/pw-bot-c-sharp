using System.Collections.Generic;
using BotCH.Core.GameFiles;

namespace BotCH.Core.Profiles;

// Данные профиля сервера — 1:1 с JSON-файлом в Profiles/*.jsonc.
// Все смещения — uint, в JSON пишутся строкой "0x...". Смещение 0 = «на этом сервере нет/не найдено»:
// такое поле не читается (иначе прочиталось бы начало объекта).
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
    public MineEssenceOffsets MineEssence { get; init; } = new();
    public MonsterEssenceOffsets MonsterEssence { get; init; } = new();
    public ElementDataOffsets ElementData { get; init; } = new();
    public InventoryOffsets Inventory { get; init; } = new();
    public PotionOffsets Potion { get; init; } = new();
    public PetFoodOffsets PetFood { get; init; } = new();
    public SkillOffsets Skill { get; init; } = new();
    public ClassSkills Skills { get; init; } = new();
    /// <summary>Инструменты для сбора ресурсов (tid кирки): без них бот не копает. Пусто — неизвестно, не копаем.</summary>
    public List<uint> GatherTools { get; init; } = new();
    public MoveTypes MoveTypes { get; init; } = new();
    public GameFilesData GameFiles { get; init; } = new();

    /// <summary>Функции клиента для прямого вызова, ключи — <see cref="GameFunctions"/>.</summary>
    public Dictionary<string, GameFunction> Functions { get; init; } = new();

    /// <summary>
    /// Функции, которые нельзя вызывать никогда (rva): выход из игры, отпустить пета.
    /// Вызов по такому адресу блокируется, даже если он по ошибке окажется в <see cref="Functions"/>.
    /// </summary>
    public Dictionary<string, uint> ForbiddenFunctions { get; init; } = new();
}

/// <summary>
/// Тип точки для SetDestination (аргумент «type» в args функции workMoveSetDestination): по прямой и с автопутём, как клик по карте.
/// </summary>
public sealed class MoveTypes
{
    /// <summary>По прямой (как клик по земле). У известных клиентов — 0.</summary>
    public uint Direct { get; init; }
    /// <summary>С автопутём, в обход препятствий. 0 — на этом сервере не найден (бег только по прямой).</summary>
    public uint Smart { get; init; }
    /// <summary>Точка в пространстве (с высотой) — для полёта. 0 — не найдена (в полёте летим только по прямой на своей высоте).</summary>
    public uint Fly { get; init; }
}

/// <summary>Файлы клиента (configs.pck и др.).</summary>
public sealed class GameFilesData
{
    /// <summary>Ключи архивов .pck; нет в профиле — стандартные.</summary>
    public PckFormat Pck { get; init; } = new();
}

public sealed class BaseOffsets
{
    /// <summary>Статический указатель от начала модуля: [модуль + BasePointer] = база.</summary>
    public uint BasePointer { get; init; }
    /// <summary>[база + Game] = game.</summary>
    public uint Game { get; init; }
    /// <summary>[база + Unfreeze] — сюда пишется 1 (unfreeze).</summary>
    public uint Unfreeze { get; init; }
    /// <summary>[база + Session] — объект связи с сервером (в нём SendGameData); this для функций с "this": "session".</summary>
    public uint Session { get; init; }
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
    /// <summary>Указатель на объект скилла, который кастуется сейчас (0 — не кастует); номер — по <see cref="SkillOffsets.Id"/>.</summary>
    public uint CastingSkill { get; init; }
    /// <summary>Байт: 0 — идёт полоска копания, иначе нет (0 в профиле — поле не найдено). Не нужен, если есть <see cref="GatherTarget"/>.</summary>
    public uint GatherIdle { get; init; }
    /// <summary>Id ресурса, который копается: не 0 — идёт копание (0 в профиле — поле не найдено, смотрим <see cref="GatherIdle"/>).</summary>
    public uint GatherTarget { get; init; }
    /// <summary>Полоска копания: сколько мс прошло.</summary>
    public uint GatherElapsed { get; init; }
    /// <summary>Полоска копания: сколько мс всего.</summary>
    public uint GatherTotal { get; init; }
    /// <summary>[перс + WorkMan] — менеджер «работ» (CECHPWorkMan).</summary>
    public uint WorkMan { get; init; }
    /// <summary>[перс + Inventory] — сумка.</summary>
    public uint Inventory { get; init; }
    /// <summary>[перс + Skills] — массив указателей на скиллы.</summary>
    public uint Skills { get; init; }
    public uint SkillsCount { get; init; }
    /// <summary>[перс + PetManager] — менеджер петов.</summary>
    public uint PetManager { get; init; }
    /// <summary>Где персонаж (int): 0 — на земле, 1 — в воде, 2 — в воздухе (как MOVEENV_* в клиенте). 0 в профиле — не найдено.</summary>
    public uint MoveEnv { get; init; }
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
    /// <summary>tid питомца (номер записи в справочнике) — для проверки, что указатель привёл в его запись.</summary>
    public uint Tid { get; init; }
    /// <summary>
    /// Указатель на запись питомца в справочнике игры (PET_ESSENCE); 0 — указателя нет: запись ищем по <see cref="Tid"/>
    /// через <see cref="ElementDataOffsets"/>.
    /// </summary>
    public uint Essence { get; init; }
    /// <summary>Название в записи (UTF-16).</summary>
    public uint EssenceName { get; init; }
    /// <summary>Где живёт (int, inhabit_type), см. <see cref="World.PetHabitats.FromGame"/>.</summary>
    public uint EssenceInhabit { get; init; }
}

public sealed class WorldOffsets
{
    /// <summary>[game + World] — мир.</summary>
    public uint World { get; init; }
    /// <summary>Сколько ячеек в хэш-таблице любого списка мира.</summary>
    public int SlotCount { get; init; }
    /// <summary>[ячейка + ObjectInSlot] — объект.</summary>
    public uint ObjectInSlot { get; init; }
    /// <summary>Мобы/NPC/петы.</summary>
    public WorldListOffsets Npcs { get; init; } = new();
    /// <summary>Предметы на земле.</summary>
    public WorldListOffsets GroundItems { get; init; } = new();
}

/// <summary>Один список мира — хэш-таблица в своём менеджере. У разных списков раскладка менеджера может отличаться.</summary>
public sealed class WorldListOffsets
{
    /// <summary>[мир + Manager] — менеджер; 0 — списка на этом сервере нет.</summary>
    public uint Manager { get; init; }
    /// <summary>[менеджер + SlotArray] — массив ячеек.</summary>
    public uint SlotArray { get; init; }
    /// <summary>[менеджер + Count] — сколько объектов в списке (для самопроверки); 0 — не читать.</summary>
    public uint Count { get; init; }
}

/// <summary>Моб/NPC/пет в мире.</summary>
public sealed class NpcOffsets
{
    public uint Wid { get; init; }
    /// <summary>6 моб, 7 NPC, 9 пет.</summary>
    public uint Type { get; init; }
    /// <summary>1 стоит, 2 физ. атака, 3 каст, 4 мёртв, 5 идёт (в боте — <see cref="World.NpcState"/>).</summary>
    public uint State { get; init; }
    /// <summary>Уровень моба (0 — поле не найдено для этого сервера).</summary>
    public uint Level { get; init; }
    /// <summary>Только у выбранной цели.</summary>
    public uint Hp { get; init; }
    /// <summary>
    /// Расстояние до персонажа, которое держит сама игра (float, по горизонтали). Бот его не читает — считает из координат;
    /// нужно только Probe selftest: сверка «расчёт = память» ловит ошибку в смещении позиции на новом клиенте. 0 — не найдено.
    /// </summary>
    public uint Distance { get; init; }
    /// <summary>WID того, кого бьёт моб; 0 — нейтрален.</summary>
    public uint Target { get; init; }
    /// <summary>
    /// WID того, в кого моб кастует (0 — поля нет). Нужен, если у кастующего <see cref="Target"/> пуст: так у заклинателей
    /// Comeback 1.4.6 — иначе бот не видит, что моб бьёт пета.
    /// </summary>
    public uint CastTarget { get; init; }
    /// <summary>
    /// WID того, кого моб бьёт рукой (0 — поля нет). Нужен, если у бьющего <see cref="Target"/> пуст: так у Comeback 1.4.6 моб,
    /// заагрившийся на перса, пока тот бил другого, — иначе бот не видит, что его бьют.
    /// </summary>
    public uint AttackTarget { get; init; }
    public uint NamePointer { get; init; }
    public uint Location { get; init; }
    /// <summary>Указатель на запись моба в справочнике игры (MONSTER_ESSENCE, см. <see cref="MonsterEssenceOffsets"/>); 0 — не найден.</summary>
    public uint Essence { get; init; }
    /// <summary>Поле флагов, где <see cref="ReturningFlag"/> — «бросил цель, возвращается (неуязвим)»; 0 — не найдено.</summary>
    public uint Returning { get; init; }
    public uint ReturningFlag { get; init; }
}

/// <summary>Запись моба в справочнике игры (MONSTER_ESSENCE): нападает ли сам и с какого расстояния.</summary>
public sealed class MonsterEssenceOffsets
{
    /// <summary>Нападает сам (int: 1 — агрессивный, 0 — нет).</summary>
    public uint Aggressive { get; init; }
    /// <summary>Радиус агра, м (int): подошли ближе — агрессивный нападает.</summary>
    public uint AggroRadius { get; init; }
}

public sealed class GroundItemOffsets
{
    public uint Id { get; init; }
    public uint Tid { get; init; }
    /// <summary>1 предмет, 2 ресурс, 3 монеты.</summary>
    public uint Kind { get; init; }
    /// <summary>
    /// Расстояние до персонажа, которое держит сама игра (float, в 3D — с высотой). Бот его не читает — считает из
    /// координат; нужно только Probe selftest (сверка «расчёт = память»). 0 — не найдено.
    /// </summary>
    public uint Distance { get; init; }
    public uint NamePointer { get; init; }
    public uint Location { get; init; }
}

/// <summary>
/// Справочник игры (elementdataman, данные из elements.data): найти запись-описание по id так же, как клиент
/// (get_data_ptr, пространство ID_SPACE_ESSENCE). Нужен, когда у объекта нет указателя на свою запись. 0 в <see cref="Manager"/> —
/// не используется.
/// </summary>
public sealed class ElementDataOffsets
{
    /// <summary>[база + Manager] — справочник; база — [модуль + <see cref="BaseOffsets.BasePointer"/>].</summary>
    public uint Manager { get; init; }
    /// <summary>Справочник + EssenceMap — хэш-таблица «id → (вид данных, номер записи)».</summary>
    public uint EssenceMap { get; init; }
    /// <summary>[таблица + MapBuckets] — массив ячеек (указатели на первый узел цепочки).</summary>
    public uint MapBuckets { get; init; }
    /// <summary>[таблица + MapBucketCount] — число ячеек; ячейка = id % число.</summary>
    public uint MapBucketCount { get; init; }
    /// <summary>Узел: +0 следующий в ячейке, +NodeKey id, +NodeType вид данных, +NodeIndex номер записи в таблице вида.</summary>
    public uint NodeKey { get; init; }
    public uint NodeType { get; init; }
    public uint NodeIndex { get; init; }
    /// <summary>[справочник + TypeBases] — массив начал таблиц по виду данных; [справочник + TypeSizes] — размеры их записей.</summary>
    public uint TypeBases { get; init; }
    public uint TypeSizes { get; init; }
}

/// <summary>
/// Запись ресурса в справочнике игры (MINE_ESSENCE из elements.data): указатель на название у предмета на земле
/// ведёт внутрь неё, запись = [название] − <see cref="NameInRecord"/>. Size = 0 — не найдено для сервера.
/// </summary>
public sealed class MineEssenceOffsets
{
    public uint NameInRecord { get; init; }
    /// <summary>Сколько байт записи читать.</summary>
    public uint Size { get; init; }
    /// <summary>tid ресурса — для проверки, что попали в нужную запись.</summary>
    public uint Id { get; init; }
    /// <summary>Чем копать (tid инструмента), 0 — без инструмента.</summary>
    public uint Tool { get; init; }
    /// <summary>Что даёт: ячейки {tid, вероятность float}.</summary>
    public uint Materials { get; init; }
    public int MaterialSlots { get; init; }
    /// <summary>Сколько за копку: {число, вероятность float} × 2.</summary>
    public uint Amounts { get; init; }
    /// <summary>Нужен взятый квест (его номер), 0 — не нужен.</summary>
    public uint Quest { get; init; }
    /// <summary>С какого уровня персонажа копается (int). 0 в профиле — поле не найдено, уровень не проверяем.</summary>
    public uint LevelRequired { get; init; }
}

public sealed class InventoryOffsets
{
    public uint Items { get; init; }
    public uint Size { get; init; }
    public uint ItemCategory { get; init; }
    public uint ItemTid { get; init; }
    public uint ItemCount { get; init; }
    /// <summary>Сколько всего влезает в стопку (0 — поле не найдено).</summary>
    public uint ItemMaxCount { get; init; }
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
    /// <summary>Аргументы в стеке, стек чистит сама функция (ret N). Так собраны функции Comeback 1.4.6.</summary>
    Stdcall,
}

public sealed class GameFunction
{
    /// <summary>Смещение от начала модуля (адрес в IDA/x32dbg = 0x400000 + Rva).</summary>
    public uint Rva { get; init; }
    /// <summary>Начало функции. Не совпало по адресу — ищем по всему коду; не нашли — не вызываем.</summary>
    public Signature? Signature { get; init; }
    public CallingConvention Convention { get; init; }
    /// <summary>Откуда взять this у thiscall: host — персонаж, session — связь с сервером (бот находит их сам, свежими на каждом вызове).</summary>
    public FunctionThis This { get; init; }
    /// <summary>
    /// Аргументы по порядку: имена значений, которые даёт бот («wid», «data», у workStart — «work»…), или числа («0», «0xFFFFFFFF»).
    /// Нет в профиле — порядок по умолчанию (как у PW Classic 1.3.6), см. методы GameCaller.
    /// </summary>
    public List<string>? Args { get; init; }
    /// <summary>Аргументы в регистрах: «ecx»/«edx» → имя значения или число (как в Args).</summary>
    public Dictionary<string, string>? Registers { get; init; }
}

public enum FunctionThis
{
    None,
    /// <summary>Персонаж: [[[модуль + база] + game] + host.struct].</summary>
    Host,
    /// <summary>Объект связи с сервером: [[модуль + база] + base.session].</summary>
    Session,
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
    public const string CancelAction = "cancelAction";
    public const string SummonPet = "summonPet";
    public const string RecallPet = "recallPet";
    public const string PetCtrl = "petCtrl";
    public const string HostApplySkill = "hostApplySkill";
    public const string HostPickupObject = "hostPickupObject";
    public const string HostFly = "hostFly";
    public const string WorkCreate = "workCreate";
    public const string WorkMoveSetDestination = "workMoveSetDestination";
    public const string WorkStart = "workStart";
}

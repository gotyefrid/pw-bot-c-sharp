using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Notes.Core.GameFiles;

namespace Notes.Core.Profiles;

// Данные профиля сервера — 1:1 с JSON-файлом в Profiles/*.jsonc.
// Все смещения — uint, в JSON пишутся строкой "0x...". Смещение 0 = «на этом сервере нет/не найдено»:
// такое поле не читается (иначе прочиталось бы начало объекта). Пустая строка "" — «не проверено на этом клиенте», тоже 0.
// Пояснения «как нашли» — комментариями в самом JSON, здесь только краткий смысл полей.

public sealed class ProfileData
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";

    public BaseOffsets Base { get; init; } = new();
    public HostOffsets Host { get; init; } = new();
    public PetManagerOffsets PetManager { get; init; } = new();
    public PetOffsets Pet { get; init; } = new();
    public WorldOffsets World { get; init; } = new();
    public NpcOffsets Npc { get; init; } = new();
    public PlayerOffsets Player { get; init; } = new();
    public GroundItemOffsets GroundItem { get; init; } = new();
    public MineEssenceOffsets MineEssence { get; init; } = new();
    /// <summary>Раскладки записей предметов с типом (оружие, банки, снаряды…) — для групп лута. Пусто — групп нет.</summary>
    public List<ItemEssenceOffsets> ItemEssences { get; init; } = new();
    /// <summary>Группы лута по типу предмета («Оружие», «Предметы ДК») или списком tid («Корм пета»): в списке лута выбираются вместо
    /// названий. Пусто — групп нет.</summary>
    public List<LootGroup> LootGroups { get; init; } = new();
    public MonsterEssenceOffsets MonsterEssence { get; init; } = new();
    public InventoryOffsets Inventory { get; init; } = new();
    public PotionOffsets Potion { get; init; } = new();
    public PetFoodOffsets PetFood { get; init; } = new();
    public SkillOffsets Skill { get; init; } = new();
    public MacroOffsets Macros { get; init; } = new();
    public AttackOffsets Attacks { get; init; } = new();
    public ChatOffsets Chat { get; init; } = new();
    public QuestOffsets Quests { get; init; } = new();
    public ClassSkills Skills { get; init; } = new();
    /// <summary>Инструменты для сбора ресурсов (tid кирки): без них бот не копает. Пусто — неизвестно, не копаем.</summary>
    public List<uint> GatherTools { get; init; } = new();
    public GatherWorkOffsets GatherWork { get; init; } = new();
    public MoveTypes MoveTypes { get; init; } = new();
    public PacketFormats Packets { get; init; } = new();
    /// <summary>Что сделать у NPC сразу после сдачи задания: номер задания → шаги.</summary>
    public Dictionary<int, QuestScript> QuestScripts { get; init; } = new();
    public UiOffsets Ui { get; init; } = new();
    public GameFilesData GameFiles { get; init; } = new();
    /// <summary>Карта пришедшего с сервера кода (<see cref="ServerCodeOffsets"/>): null — на этом клиенте не проверяли.</summary>
    public ServerCodeOffsets? ServerCode { get; init; }

    /// <summary>Функции клиента для прямого вызова, ключи — <see cref="GameFunctions"/>.</summary>
    public Dictionary<string, GameFunction> Functions { get; init; } = new();

    /// <summary>
    /// Рецепт правки exe клиента для билда этого сервера: null — правки не нужны (PW Classic)
    /// или рецепт для его билда ещё не собран (comeback136).
    /// </summary>
    public ClientPatchRecipe? Patch { get; init; }

    /// <summary>
    /// Сторож клиента (pwprotector) для проверки перед подключением: null — файл не проверен на этом клиенте.
    /// </summary>
    public ProtectorSpec? Protector { get; init; }
}

/// <summary>
/// Окна игры: менеджеры окон = [game + Managers]; окна в мире (CECGameUIMan) = [менеджеры + InGame], если
/// [менеджеры + Active] = 1 (сейчас в мире, а не на экране входа); окно разговора с NPC = [окна в мире + NpcDialog].
/// 0 — не найдено.
/// </summary>
public sealed class UiOffsets
{
    public uint Managers { get; init; }
    public uint InGame { get; init; }
    public uint Active { get; init; }
    public uint NpcDialog { get; init; }
}

/// <summary>Устройство пакетов, которые бот собирает сам (где у команды нет своей функции в клиенте).</summary>
/// <summary>
/// Скрипт задания: шаги сверх обычного «взять — сделать — сдать». Шаг — <c>{ "kind": вид, поля }</c>; виды и их поля — в
/// Brain/Quests (QuestStepKinds), здесь шаги хранятся как есть. Новый момент скрипта (например, «после взятия») — новое поле.
/// </summary>
public sealed class QuestScript
{
    /// <summary>Сразу после сдачи, пока окно разговора с NPC открыто; по порядку.</summary>
    public List<JObject> After { get; init; } = new();
}

public sealed class PacketFormats
{
    /// <summary>
    /// Размер записи в пакете продажи: 0x10 — tid, ячейка, сколько, цена штуки; 0xC — без цены. 0 — не найдено (продажа недоступна).
    /// </summary>
    public uint SellEntrySize { get; init; }

    /// <summary>Номер услуги NPC «выучить скилл» (данные — номер скилла). 0 — не найдено.</summary>
    public uint LearnSkillService { get; init; }

    /// <summary>Номер услуги NPC «вывести питомца из яйца» (данные — ячейка яйца в сумке и его tid). 0 — не найдено.</summary>
    public uint HatchPetService { get; init; }
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

/// <summary>
/// Копает ли — по текущей «работе» перса, как сама игра (где нет <see cref="HostOffsets.GatherTarget"/>): работа
/// [[перс + <see cref="HostOffsets.WorkMan"/>] + <see cref="Current"/>] типа <see cref="Gather"/> с байтом <see cref="Flag"/> не 0.
/// <see cref="Current"/> 0 — не так на этом сервере.
/// </summary>
public sealed class GatherWorkOffsets
{
    /// <summary>[менеджер работ + Current] — работа, которая идёт сейчас (0 — никакой).</summary>
    public uint Current { get; init; }
    /// <summary>
    /// Не 0 — работы лежат списком: [менеджер + Current] — массив работ, [менеджер + CurrentCount] — их число, копание — первая
    /// в массиве (так у client.exe PW Classic: свой список у каждого уровня работ).
    /// </summary>
    public uint CurrentCount { get; init; }
    /// <summary>[работа + Type] — её тип.</summary>
    public uint Type { get; init; }
    /// <summary>Тип работы «сбор».</summary>
    public uint Gather { get; init; }
    /// <summary>Байт работы «сбор»: не 0 — идёт копание.</summary>
    public uint Flag { get; init; }
}

/// <summary>Файлы клиента (configs.pck и др.).</summary>
public sealed class GameFilesData
{
    /// <summary>Ключи архивов .pck; нет в профиле — стандартные.</summary>
    public PckFormat Pck { get; init; } = new();
    /// <summary>
    /// Тексты дополнительных свойств вещей: тип свойства (из item_ext_prop.txt) → [фраза клиента, как получить каждое число].
    /// Пусто — не найдено (бот покажет номер свойства и числа).
    /// </summary>
    public Dictionary<int, List<string>> AddonTexts { get; init; } = new();
}

/// <summary>От базы. Где она сама, бот находит в коде клиента (<see cref="BasePointerSearch"/>) — в профиле её нет.</summary>
public sealed class BaseOffsets
{
    /// <summary>[база + Game] = game.</summary>
    public uint Game { get; init; }
    /// <summary>[база + Session] — объект связи с сервером (в нём SendGameData); this для функций с "this": "session".</summary>
    public uint Session { get; init; }
    /// <summary>[база + Configs] — настройки игры: в них макросы персонажа (<see cref="MacroOffsets"/>).</summary>
    public uint Configs { get; init; }
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
    /// <summary>Опыт на текущем уровне (int). 0 — не найдено.</summary>
    public uint Exp { get; init; }
    /// <summary>
    /// Код, который читает таблицу «опыт до следующего уровня» по уровню перса: на месте <see cref="ExpTableAt"/> — адрес X,
    /// нужно на уровне L — [X + L × 4]. null — не найдено для сервера.
    /// </summary>
    public Signature? ExpTableFind { get; init; }
    public uint ExpTableAt { get; init; }
    /// <summary>WID текущей цели.</summary>
    public uint TargetId { get; init; }
    /// <summary>Перезарядка корма пета, мс осталось (0 — можно кормить).</summary>
    public uint PetFoodCooldown { get; init; }
    /// <summary>Координаты: X, высота, Y — три float подряд.</summary>
    public uint Location { get; init; }
    /// <summary>Указатель на объект скилла, который кастуется сейчас (0 — не кастует); номер — по <see cref="SkillOffsets.Id"/>.</summary>
    public uint CastingSkill { get; init; }
    /// <summary>Id ресурса, который копается: не 0 — идёт копание. Пишется не при каждом копании — главный признак <see cref="ProfileData.GatherWork"/>; 0 в профиле — поле не найдено.</summary>
    public uint GatherTarget { get; init; }
    /// <summary>Полоска копания: сколько мс прошло.</summary>
    public uint GatherElapsed { get; init; }
    /// <summary>Полоска копания: сколько мс всего.</summary>
    public uint GatherTotal { get; init; }
    /// <summary>[перс + WorkMan] — менеджер «работ» (CECHPWorkMan).</summary>
    public uint WorkMan { get; init; }
    /// <summary>[перс + Inventory] — сумка.</summary>
    public uint Inventory { get; init; }
    /// <summary>[перс + Bank] — основная вкладка банка (хранилища); устроена как сумка. 0 — не найдено.</summary>
    public uint Bank { get; init; }
    /// <summary>[перс + Equipment] — надетые вещи (ячейка = место: 0 — оружие…); устроены как сумка. 0 — не найдено.</summary>
    public uint Equipment { get; init; }
    /// <summary>[перс + QuestItems] — сумка заданий (предметы заданий); устроена как сумка. 0 — не найдено.</summary>
    public uint QuestItems { get; init; }
    /// <summary>[перс + Quests] — объект заданий персонажа (<see cref="QuestOffsets"/>). 0 — не найдено.</summary>
    public uint Quests { get; init; }
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
    /// <summary>Указатель на запись питомца в справочнике игры (PET_ESSENCE); 0 — не найдено: название и среда не читаются.</summary>
    public uint Essence { get; init; }
    /// <summary>Название в записи (UTF-16).</summary>
    public uint EssenceName { get; init; }
    /// <summary>Где живёт (int, inhabit_type), см. <see cref="World.PetHabitats.FromGame"/>.</summary>
    public uint EssenceInhabit { get; init; }
    /// <summary>Уровень (int); 0 — не найдено: HP в единицах не считается.</summary>
    public uint Level { get; init; }
    /// <summary>
    /// Для максимума HP, как в окне пета: (уровень − LevelRequire × HpB + HpC) × HpA. LevelRequire — int, остальное — float.
    /// </summary>
    public uint EssenceLevelRequire { get; init; }
    public uint EssenceHpA { get; init; }
    public uint EssenceHpB { get; init; }
    public uint EssenceHpC { get; init; }
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
    /// <summary>Другие игроки рядом (без нашего персонажа).</summary>
    public WorldListOffsets Players { get; init; } = new();
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
    /// <summary>Вид моба или NPC (tid), как в заданиях и файлах клиента; 0 — не найдено для этого сервера.</summary>
    public uint Tid { get; init; }
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
    /// <summary>
    /// WID того, на кого идёт пет (0 — поля нет). Читается только у петов, если <see cref="Target"/> пуст: так у client.exe
    /// PW Classic, где общего поля цели нет, а пет по приказу бежит к цели и бьёт — иначе бот не видит, что пет взял цель.
    /// </summary>
    public uint PetTarget { get; init; }
    public uint NamePointer { get; init; }
    public uint Location { get; init; }
    /// <summary>Указатель на запись моба в справочнике игры (MONSTER_ESSENCE, см. <see cref="MonsterEssenceOffsets"/>); 0 — не найден.</summary>
    public uint Essence { get; init; }
    /// <summary>Поле флагов, где <see cref="ReturningFlag"/> — «бросил цель, возвращается (неуязвим)»; 0 — не найдено.</summary>
    public uint Returning { get; init; }
    public uint ReturningFlag { get; init; }
}

/// <summary>
/// Принятые задания: [перс + <see cref="HostOffsets.Quests"/>] — объект заданий, в нём список принятых (как в окне «Задания»).
/// Запись списка ссылается на запись задания из tasks.data, которую игра держит в памяти: название, цель, кому сдать.
/// Смещения в записи списка не выровнены (поля подряд, без пропусков).
/// </summary>
public sealed class QuestOffsets
{
    /// <summary>[объект заданий + Active] — список принятых: байт +0 — сколько в нём записей.</summary>
    public uint Active { get; init; }
    /// <summary>С какого места списка идут записи.</summary>
    public uint Entries { get; init; }
    public uint EntrySize { get; init; }
    /// <summary>Номер задания (ushort).</summary>
    public uint EntryId { get; init; }
    /// <summary>Байт: номер записи-родителя в списке (0xFF — задание верхнего уровня).</summary>
    public uint EntryParent { get; init; }
    /// <summary>Указатель на запись задания.</summary>
    public uint EntryTemplate { get; init; }
    /// <summary>Сколько убито: ushort на каждого моба из цели, по порядку.</summary>
    public uint EntryKills { get; init; }

    /// <summary>Номер задания в записи — для проверки, что попали в нужную.</summary>
    public uint Id { get; init; }
    /// <summary>Название (UTF-16, может начинаться с кода цвета «^RRGGBB»).</summary>
    public uint Name { get; init; }
    /// <summary>Тип цели (int): 1 — убить мобов, 2 — собрать предметы, 3 — поговорить с NPC (<see cref="World.QuestGoals"/>).</summary>
    public uint Goal { get; init; }
    /// <summary>Кто даёт задание — tid NPC («Персонаж, дающий задание» в окне); 0 — не найдено.</summary>
    public uint GiverNpc { get; init; }
    /// <summary>Кому сдать — tid NPC (как «Персонаж, дающий награду» в окне); 0 — некому.</summary>
    public uint AwardNpc { get; init; }
    /// <summary>Мобы цели: число (int) и указатель на массив записей по <see cref="MonsterSize"/> байт: +0 tid моба, +4 сколько убить.</summary>
    public uint MonsterCount { get; init; }
    public uint Monsters { get; init; }
    public uint MonsterSize { get; init; }
    /// <summary>В записи моба цели: tid предмета, который падает с моба в сумку заданий, и сколько его нужно; 0 — не найдено.</summary>
    public uint MonsterDrop { get; init; }
    public uint MonsterDropNeed { get; init; }
    /// <summary>Предметы цели: число (int) и указатель на массив записей по <see cref="ItemSize"/> байт: +0 tid, +<see cref="ItemNeed"/> сколько.</summary>
    public uint ItemCount { get; init; }
    public uint Items { get; init; }
    public uint ItemSize { get; init; }
    public uint ItemNeed { get; init; }

    /// <summary>
    /// Код, который пишет глобальную переменную с объектом списков заданий; в нём на месте <see cref="AvailableAt"/> — адрес
    /// переменной. null — не найдено для сервера.
    /// </summary>
    public Signature? AvailableFind { get; init; }
    public uint AvailableAt { get; init; }
    /// <summary>[объект + Available] — доступные задания (вкладка «Доступные»): начало, +4 конец массива указателей на записи.</summary>
    public uint Available { get; init; }
    /// <summary>[объект + All] — все задания верхнего уровня: начало, +4 конец массива указателей на записи (из них окно отбирает «Доступные»).</summary>
    public uint All { get; init; }
    /// <summary>В записи задания: уровень персонажа от и до (int, 0 — без границы), байт «уровень — перерождения» (не 0 — такие пропускаем).</summary>
    public uint MinLevel { get; init; }
    public uint MaxLevel { get; init; }
    public uint RebirthLevel { get; init; }
    /// <summary>В записи задания: указатели на первое подзадание и на следующее рядом (0 — нет).</summary>
    public uint FirstChild { get; init; }
    public uint NextSibling { get; init; }
    /// <summary>В записи задания: байт «выполнить одно из подзаданий» — NPC выдаёт подзадание, а не само задание.</summary>
    public uint ChooseOne { get; init; }
}

/// <summary>
/// Сообщения чата — список, который показывает окно чата (последние ~200). Где он, бот находит по коду клиента
/// (<see cref="CodeDataSearch"/>): [список] — начало массива записей, [список + 4] — конец.
/// </summary>
public sealed class ChatOffsets
{
    /// <summary>Код, который работает со списком; в нём на месте <see cref="At"/> — адрес списка. null — не найдено для сервера.</summary>
    public Signature? Find { get; init; }
    public uint At { get; init; }
    /// <summary>Размер записи о сообщении.</summary>
    public uint EntrySize { get; init; }
    /// <summary>Канал (младший байт).</summary>
    public uint Channel { get; init; }
    /// <summary>Указатель на текст (UTF-16, с кодами цвета «^RRGGBB»).</summary>
    public uint Text { get; init; }
    /// <summary>Номер сообщения: у каждого следующего на 1 больше.</summary>
    public uint Number { get; init; }
    /// <summary>WID отправителя (у системных −1).</summary>
    public uint Sender { get; init; }
    /// <summary>
    /// Фраза игры после смерти персонажа, «%s» — кто убил (игрок или моб), без кодов цвета. Пусто — не знаем.
    /// </summary>
    public string KilledBy { get; init; } = "";
    /// <summary>Фраза игры после гибели пета (кто убил, не пишет). Пусто — не знаем.</summary>
    public string PetDied { get; init; } = "";
}

/// <summary>
/// Другой игрок рядом. Уровень, HP и цель игра знает не про всех (у остальных 0) — сервер присылает их не всегда.
/// </summary>
public sealed class PlayerOffsets
{
    public uint Wid { get; init; }
    public uint NamePointer { get; init; }
    public uint Location { get; init; }
    public uint Level { get; init; }
    public uint Hp { get; init; }
    public uint MaxHp { get; init; }
    /// <summary>WID того, кого игрок держит в цели; 0 — никого или неизвестно.</summary>
    public uint Target { get; init; }
    /// <summary>Класс (int, номер как в игре: 0 воин … 5 убийца … 7 жрец, см. <see cref="World.PlayerClasses"/>).</summary>
    public uint Class { get; init; }
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
    /// <summary>Байт: 1 предмет, 2 ресурс, 3 монеты. Следующий байт — не вид (у части вещей там 1, всё поле — 0x101).</summary>
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

/// <summary>
/// Запись предмета с типом в справочнике игры (WEAPON_ESSENCE, MATERIAL_ESSENCE, PROJECTILE_ESSENCE… из elements.data):
/// указатель на название у предмета на земле ведёт внутрь неё, запись = [название] − <see cref="NameInRecord"/>.
/// </summary>
public sealed class ItemEssenceOffsets
{
    public uint NameInRecord { get; init; }
    /// <summary>tid предмета — для проверки, что раскладка та: у записи другого вида здесь чужое число.</summary>
    public uint Id { get; init; }
    /// <summary>Тип («Мечи», «Восстанавливает ЖС», «Стрела») — по нему группа лута.</summary>
    public uint Type { get; init; }
}

/// <summary>Группа лута: все предметы типов <see cref="Types"/> и с tid из <see cref="Tids"/> под одним названием в списке лута.</summary>
public sealed class LootGroup
{
    public string Name { get; init; } = "";
    public List<uint> Types { get; init; } = new();
    /// <summary>Предметы списком tid — когда в их записи нет типа (корм пета).</summary>
    public List<uint> Tids { get; init; } = new();
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
    /// <summary>Цена продажи одной штуки в лавке (0 — поле не найдено).</summary>
    public uint ItemPrice { get; init; }
    /// <summary>Прочность вещи — текущая и наибольшая, в сотых (в игре 46/66 — здесь 4600/6600); 0 — поле не найдено.</summary>
    public uint ItemDurability { get; init; }
    public uint ItemMaxDurability { get; init; }
    /// <summary>Флаги вещи и бит «не чинится» в них (клиент пропускает такую вещь в «Починить все»); 0 — не найдено.</summary>
    public uint ItemFlags { get; init; }
    public uint FlagNoRepair { get; init; }
    /// <summary>Куда надевается (маска мест, 8 байт): не 0 — это вещь, у неё прочность, ячейки и свойства; 0 — поле не найдено.</summary>
    public uint ItemEquipMask { get; init; }
    /// <summary>Ячейки под камни: указатель на массив tid (по 4 байта, 0 — пустая) и их число.</summary>
    public uint ItemSockets { get; init; }
    public uint ItemSocketCount { get; init; }
    /// <summary>Дополнительные свойства: указатель на массив записей и их число; в записи — номер свойства (+0), сколько чисел, числа.</summary>
    public uint ItemAddons { get; init; }
    public uint ItemAddonCount { get; init; }
    public uint AddonSize { get; init; }
    public uint AddonParamCount { get; init; }
    public uint AddonParams { get; init; }
    /// <summary>Описание (шаблон) банки и большинства предметов.</summary>
    public uint ItemEssence { get; init; }
    /// <summary>Описание корма пета (у него другой класс предмета).</summary>
    public uint FoodEssence { get; init; }
    public int CategoryPotion { get; init; }
    public int CategoryPetFood { get; init; }
    /// <summary>Категория яйца питомца; 0 — не найдено.</summary>
    public uint CategoryPetEgg { get; init; }
    /// <summary>Сколько байт предмета просматривать в поисках указателя на его описание (0 — названий и групп у предметов нет).</summary>
    public uint EssenceSearch { get; init; }
    /// <summary>Где название в описании без типа (tid +0, название сразу за ним).</summary>
    public uint PlainName { get; init; }
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

/// <summary>Описание корма (+0 — tid корма).</summary>
public sealed class PetFoodOffsets
{
    public uint Loyalty { get; init; }
    /// <summary>Название (UTF-16); 0 — не читаем.</summary>
    public uint Name { get; init; }
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

/// <summary>
/// Макросы персонажа (собирает игрок в окне скиллов) — в настройках игры: [[база + <see cref="BaseOffsets.Configs"/>] + <see cref="List"/>],
/// <see cref="Count"/> подряд по <see cref="Size"/> байт. Первый байт макроса — номер иконки, 0 — макроса нет. Бот только
/// запускает макрос (<see cref="GameFunctions.HostApplyComboSkill"/>), что внутри — не читает. <see cref="List"/> 0 — не найдено.
/// </summary>
public sealed class MacroOffsets
{
    public uint List { get; init; }
    public uint Size { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// Удары, которые игра сейчас показывает (менеджер атак мира): кто, кого, сколько урона. Узел списка: +0x0 следующий; у цели
/// первое поле — WID. <see cref="Manager"/> 0 — не найдено.
/// </summary>
public sealed class AttackOffsets
{
    /// <summary>[мир + Manager] — менеджер атак.</summary>
    public uint Manager { get; init; }
    /// <summary>[менеджер + Head] — первый узел списка ударов.</summary>
    public uint Head { get; init; }
    /// <summary>Удар в узле — с этого смещения; дальше смещения от него.</summary>
    public uint Event { get; init; }
    /// <summary>Сколько мс удар уже в списке.</summary>
    public uint Lived { get; init; }
    /// <summary>WID того, кто бьёт.</summary>
    public uint Attacker { get; init; }
    /// <summary>Скилл; 0 — обычная атака.</summary>
    public uint Skill { get; init; }
    /// <summary>Адрес массива целей.</summary>
    public uint Targets { get; init; }
    public uint TargetCount { get; init; }
    /// <summary>Сколько байт одна цель в массиве.</summary>
    public uint TargetSize { get; init; }
    /// <summary>В цели: флаги удара.</summary>
    public uint TargetFlags { get; init; }
    /// <summary>В цели: урон.</summary>
    public uint TargetDamage { get; init; }
}

/// <summary>
/// Карта пришедшего с сервера кода: приёмник серверных команд античита (1.4.6 — 0x4B9300) регистрирует каждый блок в
/// std::map, ключ — слово из пакета, значение — исполняемый буфер. Карта живёт до конца сеанса, поэтому бот её только
/// читает (Probe code): что сервер прислал и когда. <see cref="MapHead"/> 0 — не проверено на этом клиенте.
/// </summary>
public sealed class ServerCodeOffsets
{
    /// <summary>
    /// Смещение головы карты от начала модуля (как rva у функций — у клиента ASLR): VA − база образа (0x400000).
    /// Найти в новом билде: приёмник — по прологу 55 8B EC 83 E4 F8 83 EC 14 53 56 8B 75 0C 57 83 FE 02 0F 82
    /// (1.4.6 — 0x4B9300), он берёт критическую секцию по 0xCF1A20 и зовёт 0x4B8CC0; поиск по ключу (0x4BA140) и
    /// вставка (0x4B9FF0) работают с одним и тем же ecx — это и есть голова (в 1.4.6 VA 0xCF1A3C).
    /// </summary>
    public uint MapHead { get; init; }
    /// <summary>Ключ от этого и выше — свои команды клиента (0xFFEA = 65514), ниже — присланные сервером.</summary>
    public uint OwnKeyMin { get; init; }
    /// <summary>Сколько байт кода читать из каждой записи (потом ищем в них строки).</summary>
    public uint CodeBytes { get; init; }
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
    /// <summary>Метод объекта, вложенного в персонаж или связь: this = (персонаж или связь) + сдвиг. Обычно 0.</summary>
    public uint ThisOffset { get; init; }
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
    /// <summary>Окно разговора с NPC (CDlgNPC): см. <see cref="UiOffsets"/>.</summary>
    NpcDialog,
    /// <summary>Объект заданий персонажа: [перс + host.quests].</summary>
    Quests,
}

/// <summary>Имена функций в профиле.</summary>
public static class GameFunctions
{
    public const string SelectTarget = "selectTarget";
    public const string Unselect = "unselect";
    public const string NormalAttack = "normalAttack";
    public const string UseItem = "useItem";
    public const string OpenRemotePanel = "openRemotePanel";
    public const string NpcHello = "npcHello";
    public const string NpcOpenStorage = "npcOpenStorage";
    public const string NpcReturnTask = "npcReturnTask";
    public const string NpcAcceptTask = "npcAcceptTask";
    public const string QuestCheck = "questCheck";
    public const string BagMove = "bagMove";
    public const string BankExchange = "bankExchange";
    public const string BankPutCount = "bankPutCount";
    public const string SendPacket = "sendPacket";
    public const string RepairItem = "repairItem";
    public const string NpcDialogClose = "npcDialogClose";
    public const string CastSkill = "castSkill";
    public const string CancelAction = "cancelAction";
    public const string SummonPet = "summonPet";
    public const string RecallPet = "recallPet";
    public const string PetCtrl = "petCtrl";
    public const string HostApplySkill = "hostApplySkill";
    public const string HostApplyComboSkill = "hostApplyComboSkill";
    public const string HostClearComboSkill = "hostClearComboSkill";
    public const string HostPickupObject = "hostPickupObject";
    public const string HostFly = "hostFly";
    public const string WorkCreate = "workCreate";
    public const string WorkMoveSetDestination = "workMoveSetDestination";
    public const string WorkStart = "workStart";
    public const string WorkTraceTarget = "workTraceTarget";
    public const string WorkTraceSetTarget = "workTraceSetTarget";
}

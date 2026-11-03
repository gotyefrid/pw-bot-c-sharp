using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Calls;

public sealed record CallResult(bool Ok, string Details)
{
    public static readonly CallResult Done = new(true, "");

    /// <summary>Что вернула функция (у вызовов с ответом — например, проверки заданий); null — ответа нет.</summary>
    public IReadOnlyList<uint>? Values { get; init; }

    public static CallResult Refused(string reason) => new(false, reason);
}

/// <summary>
/// Действия бота прямыми вызовами функций клиента по профилю сервера (работают и при неактивном окне игры).
/// Правила безопасности — здесь, в одном месте:
/// <list type="bullet">
/// <item>функция должна быть найдена (<see cref="FunctionResolver"/>: Found/Relocated);</item>
/// <item>перед КАЖДЫМ вызовом её первые байты сверяются с сигнатурой ещё раз.</item>
/// </list>
/// Функции профиля разбираются один раз, при создании: откуда this, как собрать каждый аргумент и регистр. Ошибка профиля
/// («аргумент, которого бот не даёт») видна сразу (<see cref="Problems"/>, в лог при «Старт»), а не посреди боя.
/// Сам вызов в игре выполняет <see cref="IRemoteRunner"/>.
/// </summary>
public sealed class GameCaller : IGameActions
{
    /// <summary>Номер приказа пету «атаковать» (как Alt+1).</summary>
    public const uint PetCommandAttack = 1;

    /// <summary>Адрес данных, записанных рядом с заглушкой.</summary>
    private const string DataArg = "data";

    /// <summary>Аргумент StartWork «созданная работа» — подставляет заглушка.</summary>
    private const string WorkArg = "work";

    /// <summary>Аргумент SetDestination «тип точки» (по прямой / автопуть / полёт), см. <see cref="MoveTypes"/>.</summary>
    private const string TypeArg = "type";

    /// <summary>
    /// Что бот даёт каждой функции: порядок аргументов по умолчанию (как у PW Classic 1.3.6 — если в профиле нет args).
    /// Имена — значения, которые бот даёт этой функции (в профиле их можно переставить, убрать или заменить числом), числа — как есть.
    /// </summary>
    private static readonly Dictionary<string, string[]> Defaults = new()
    {
        [GameFunctions.SelectTarget] = ["wid"],
        [GameFunctions.Unselect] = [],
        [GameFunctions.NormalAttack] = ["pvpMask"],
        [GameFunctions.UseItem] = ["where", "slot", "tid", "count"],
        [GameFunctions.OpenRemotePanel] = ["panel"],
        [GameFunctions.NpcHello] = ["wid"],
        // Пароль банка — строка по адресу данных
        [GameFunctions.NpcOpenStorage] = [DataArg],
        [GameFunctions.NpcReturnTask] = ["task", "choice"],
        [GameFunctions.NpcAcceptTask] = ["task", "storage", "refresh"],
        [GameFunctions.QuestCheck] = ["task"],
        [GameFunctions.BagMove] = ["from", "to", "count"],
        [GameFunctions.BankExchange] = ["tab", "bankSlot", "slot"],
        [GameFunctions.BankPutCount] = ["tab", "bankSlot", "slot", "count"],
        // Пакет по адресу данных и его длина
        [GameFunctions.SendPacket] = [DataArg, "size"],
        // Что чинить: tid предмета (-1 — всё), где он и в какой ячейке
        [GameFunctions.RepairItem] = ["where", "slot", "tid"],
        [GameFunctions.NpcDialogClose] = [],
        // Номер колесницы (воскрешение на поле боя за город); обычная смерть — 0
        [GameFunctions.ReviveInTown] = ["0"],
        [GameFunctions.SummonPet] = ["index"],
        [GameFunctions.RecallPet] = [],
        [GameFunctions.CastSkill] = ["skill", "pvpMask", "count", DataArg],
        [GameFunctions.CancelAction] = [],
        [GameFunctions.PetCtrl] = ["target", "command", DataArg, "size"],
        // Второй — «шаг макроса»: так макрос жмёт свои скиллы; 0 — нажат не макросом, и игра заодно убирает идущий макрос
        [GameFunctions.HostApplySkill] = ["skill", "fromMacro", "target", "pvp"],
        // Как кнопка макроса на панели: номер с 0, повтор не пропускать, «атаковать силой» — как в настройках игры
        [GameFunctions.HostApplyComboSkill] = ["group", "0", "0xFFFFFFFF"],
        [GameFunctions.HostClearComboSkill] = [],
        [GameFunctions.HostPickupObject] = ["id", "gather"],
        [GameFunctions.HostFly] = ["force"],
        // 1.3.6: SetDestination(type, &point), StartWork(1, work, 1, 0)
        [GameFunctions.WorkMoveSetDestination] = [TypeArg, DataArg],
        [GameFunctions.WorkStart] = ["1", WorkArg, "1", "0"],
    };

    /// <summary>Методы, у которых this подставляет сам бот (менеджер работ, созданная работа), а не профиль.</summary>
    private static readonly HashSet<string> OwnThis =
        [GameFunctions.WorkCreate, GameFunctions.WorkMoveSetDestination, GameFunctions.WorkStart, GameFunctions.WorkTraceTarget, GameFunctions.WorkTraceSetTarget];

    /// <summary>Причина работы «подойти»: действовать с выбранной целью — с NPC заговорить, моба атаковать (второй клик по цели).</summary>
    private const uint TraceInteract = 2;

    /// <summary>Откуда взять аргумент: имя значения бота (в том числе адрес данных и работа) или число (<see cref="Name"/> = null).</summary>
    private readonly record struct Arg(string? Name, uint Number);

    /// <summary>Функция, разобранная при создании: аргументы и регистры по порядку; <see cref="Problem"/> — почему вызывать нельзя.</summary>
    private sealed record Prepared(GameFunction Definition, IReadOnlyList<Arg> Args, Arg? Ecx, Arg? Edx, string? Problem);

    private readonly IMemory _memory;
    private readonly IRemoteRunner _runner;
    private readonly GameRoots _roots;
    private readonly ProfileData _profile;
    private readonly Dictionary<string, FunctionLocation> _functions;
    private readonly Dictionary<string, Prepared> _prepared;

    public GameCaller(IMemory memory, IRemoteRunner runner, uint moduleBase, GameRoots roots, ProfileData profile, FunctionResolver? resolver = null)
    {
        _memory = memory;
        _runner = runner;
        _roots = roots;
        _profile = profile;
        resolver ??= new FunctionResolver(memory, moduleBase);
        _functions = profile.Functions.ToDictionary(f => f.Key, f => resolver.Resolve(f.Key, f.Value));
        _prepared = profile.Functions.ToDictionary(f => f.Key, f => Prepare(f.Key, f.Value));
        Capabilities = Capabilities.From(profile, WhyNot);
    }

    public string Mode => "вызовы";

    /// <summary>Что можно делать в этом клиенте — по тому, что реально найдено (а не только заявлено в профиле).</summary>
    public Capabilities Capabilities { get; }

    /// <summary>Можно ли вызывать функцию (есть в профиле, найдена, профиль описывает её без ошибок).</summary>
    public bool Can(string name) => WhyNot(name) is null;

    /// <summary>Функции профиля, которые вызывать нельзя, и почему — для лога при «Старт».</summary>
    public IReadOnlyList<(string Name, string Why)> Problems
        => _profile.Functions.Keys.Select(name => (Name: name, Why: WhyNot(name))).Where(p => p.Why is not null).Select(p => (p.Name, p.Why!)).ToList();

    // Почему функцию нельзя вызывать; null — можно
    private string? WhyNot(string name)
        => !_functions.TryGetValue(name, out var f) ? $"в профиле нет функции {name}"
            : !f.IsUsable ? $"{name}: {f.Details}"
            : _prepared[name].Problem is { } problem ? $"{name}: {problem}"
            : null;

    // ── Действия ─────────────────────────────────────────────────────────────

    public CallResult SelectTarget(uint wid) => Call(GameFunctions.SelectTarget, null, ("wid", wid));

    public CallResult Unselect() => Call(GameFunctions.Unselect, null);

    /// <summary>Обычная атака текущей цели, pvpMask 0.</summary>
    public CallResult NormalAttack() => Call(GameFunctions.NormalAttack, null, ("pvpMask", 0));

    /// <summary>Использовать 1 предмет из ячейки основной сумки (банка, корм пета).</summary>
    public CallResult UseItem(InventoryItem item)
        => Call(GameFunctions.UseItem, null, ("where", 0), ("slot", (uint)item.Slot), ("tid", item.Tid), ("count", 1));

    /// <summary>Открыть банк, как кнопкой в сумке: сервер сам отвечает окном, работает и в бою.</summary>
    public CallResult OpenBank() => Call(GameFunctions.OpenRemotePanel, null, ("panel", 1));

    /// <summary>Открыть лавку (торговля и ремонт), как кнопкой в сумке: та же команда, панель 3; окно открывает ответ сервера.</summary>
    public CallResult OpenShop() => Call(GameFunctions.OpenRemotePanel, null, ("panel", 3));

    /// <summary>
    /// Закрыть окно разговора с NPC, как кнопкой «Назад» (или Esc): клиент прячет окно у себя, серверу ничего не шлёт —
    /// разговор сервер помнит и дальше (продажа и ремонт проходят).
    /// </summary>
    public CallResult NpcDialogClose() => Call(GameFunctions.NpcDialogClose, null);

    /// <summary>Встать в ближайшем городе после смерти, как верхней кнопкой окна смерти (опыт теряется, как у игрока).</summary>
    public CallResult ReviveInTown() => Call(GameFunctions.ReviveInTown, null);

    /// <summary>Начать разговор с NPC, как кликом по нему: сервер отвечает списком услуг, клиент открывает окно NPC.</summary>
    public CallResult NpcHello(uint wid) => Call(GameFunctions.NpcHello, null, ("wid", wid));

    /// <summary>
    /// Открыть банк у банкира, как пунктом «Услуги хранения» в его окне: стоя рядом с ним, разговор (<see cref="NpcHello"/>) не нужен.
    /// Пароль пустой — как у клиента, когда пароль на банк не стоит; окно открывает ответ сервера.
    /// </summary>
    public CallResult NpcOpenBank() => Call(GameFunctions.NpcOpenStorage, new byte[4]);

    /// <summary>
    /// Сдать задание NPC, с которым начат разговор (<see cref="NpcHello"/>), как пунктом «сдать» в его окне. Награда — первая
    /// (номер 0): так клиент сдаёт задания без выбора награды.
    /// </summary>
    public CallResult NpcReturnTask(int taskId) => Call(GameFunctions.NpcReturnTask, null, ("task", (uint)taskId), ("choice", 0));

    /// <summary>
    /// Взять задание у NPC, с которым начат разговор (<see cref="NpcHello"/>), как пунктом задания в его окне. Склад и обновление —
    /// 0: обычное задание, не с «доски заданий».
    /// </summary>
    public CallResult NpcAcceptTask(int taskId) =>
        Call(GameFunctions.NpcAcceptTask, null, ("task", (uint)taskId), ("storage", 0), ("refresh", 0));

    /// <summary>
    /// Выучить скилл у NPC, с которым начат разговор, как в его окне навыков. Учебник (если скилл его требует) сервер берёт из
    /// сумки сам.
    /// </summary>
    public CallResult NpcLearnSkill(int skillId)
        => skillId <= 0
            ? CallResult.Refused($"выучить скилл: неверный номер {skillId}")
            : NpcService(_profile.Packets.LearnSkillService, "выучить скилл (packets.learnSkillService)", (uint)skillId);

    /// <summary>
    /// Вывести питомца из яйца у NPC, с которым начат разговор, как окном «Инкубация питомца»: яйцо — в ячейке сумки
    /// <paramref name="slot"/>. Что это яйцо и что в клетке есть место, проверяет тот, кто зовёт.
    /// </summary>
    public CallResult NpcHatchPet(int slot, uint eggTid)
        => slot < 0 || eggTid == 0
            ? CallResult.Refused($"вывести питомца: неверная ячейка {slot} или яйцо {eggTid}")
            : NpcService(_profile.Packets.HatchPetService, "вывести питомца (packets.hatchPetService)", (uint)slot, eggTid);

    private CallResult NpcService(uint service, string what, params uint[] data)
    {
        if (service == 0)
            return CallResult.Refused($"{what}: номер услуги NPC не найден");

        var packet = NpcServicePacket(service, data);
        return Call(GameFunctions.SendPacket, packet, ("size", (uint)packet.Length));
    }

    /// <summary>Пакет услуги NPC, как у клиента: [0x25:2][услуга:4][длина данных:4], дальше данные по 4 байта.</summary>
    public static byte[] NpcServicePacket(uint service, params uint[] data)
    {
        var packet = new byte[10 + 4 * data.Length];
        Buffer.BlockCopy(BitConverter.GetBytes(NpcServiceCommand), 0, packet, 0, 2);
        Buffer.BlockCopy(BitConverter.GetBytes(service), 0, packet, 2, 4);
        Buffer.BlockCopy(BitConverter.GetBytes((uint)(4 * data.Length)), 0, packet, 6, 4);
        for (var i = 0; i < data.Length; i++)
            Buffer.BlockCopy(BitConverter.GetBytes(data[i]), 0, packet, 10 + 4 * i, 4);
        return packet;
    }

    /// <summary>Сколько заданий проверять одним вызовом: номер и ответ — по 4 байта, данные вызова — одна страница.</summary>
    public const int QuestChecksPerCall = 500;

    /// <summary>
    /// Можно ли взять задания сейчас — проверка самой игры (как у вкладки «Доступные» и окна NPC): по ответу на номер, 0 — можно,
    /// иначе код причины. Ничего не шлёт серверу. Много заданий — несколькими вызовами по <see cref="QuestChecksPerCall"/>.
    /// </summary>
    public CallResult QuestChecks(IReadOnlyList<int> taskIds)
    {
        const string name = GameFunctions.QuestCheck;
        if (taskIds.Count == 0)
            return CallResult.Done with { Values = [] };
        if (Check(name) is { } refused)
            return refused;
        if (_prepared[name].Definition.Convention != CallingConvention.Thiscall || _prepared[name].Definition.This != FunctionThis.Quests)
            return CallResult.Refused($"{name}: в профиле ждём thiscall с this = quests");
        if (!TryQuests(out var quests))
            return CallResult.Refused($"{name}: объект заданий персонажа не найден (host.quests)");

        var address = _functions[name].Address;
        var answers = new List<uint>(taskIds.Count);
        for (var from = 0; from < taskIds.Count; from += QuestChecksPerCall)
        {
            var count = Math.Min(QuestChecksPerCall, taskIds.Count - from);
            // Данные: номера, за ними место под ответы
            var data = new byte[count * 8];
            for (var i = 0; i < count; i++)
                Buffer.BlockCopy(BitConverter.GetBytes((uint)taskIds[from + i]), 0, data, i * 4, 4);
            var run = _runner.Run(data, at => StubBuilder.CallEach(address, quests, at, at + (uint)count * 4, count));
            if (!run.IsDone)
                return CallResult.Refused(run.Details);
            if (run.Data is not { } back || back.Length != data.Length)
                return CallResult.Refused($"{name}: ответы не прочитались");
            for (var i = 0; i < count; i++)
                answers.Add(BitConverter.ToUInt32(back, (count + i) * 4));
        }

        return CallResult.Done with { Values = answers };
    }

    /// <summary>Номер вкладки банка в командах переноса: основная (данные игры, одинаковы на всех клиентах Comeback).</summary>
    private const uint BankMainTab = 3;

    /// <summary>Перенести <paramref name="count"/> штук из ячейки сумки в другую ячейку сумки; в пустую — разделить стопку.</summary>
    public CallResult BagMove(int fromSlot, int toSlot, int count)
        => Call(GameFunctions.BagMove, null, ("from", (uint)fromSlot), ("to", (uint)toSlot), ("count", (uint)count));

    /// <summary>
    /// Поменять местами ячейку сумки и ячейку банка (основная вкладка): стопка уходит целиком; в пустую ячейку — это перенос.
    /// </summary>
    public CallResult BankExchange(int bagSlot, int bankSlot)
        => Call(GameFunctions.BankExchange, null, ("tab", BankMainTab), ("bankSlot", (uint)bankSlot), ("slot", (uint)bagSlot));

    /// <summary>
    /// Доложить <paramref name="count"/> штук из ячейки сумки на стопку того же предмета в ячейке банка (основная вкладка):
    /// стопки сливаются, как мышкой.
    /// </summary>
    public CallResult BankPutCount(int bagSlot, int bankSlot, int count)
        => Call(GameFunctions.BankPutCount, null, ("tab", BankMainTab), ("bankSlot", (uint)bankSlot), ("slot", (uint)bagSlot),
            ("count", (uint)count));

    /// <summary>Команда «услуга NPC» и номер услуги «продать» (данные игры).</summary>
    private const ushort NpcServiceCommand = 0x25;
    private const uint NpcServiceSell = 2;

    /// <summary>Размер записи продажи с ценой штуки и без неё (<see cref="PacketFormats.SellEntrySize"/>).</summary>
    public const int SellEntryWithPrice = 0x10, SellEntryNoPrice = 0xC;

    /// <summary>
    /// Продать в лавке: по строке на ячейку сумки — сколько штук. Как кнопка «Продать» с «Да»: корзину окна клиент серверу не
    /// шлёт, только этот пакет; цена одной штуки — из предмета (<see cref="InventoryItem.Price"/>), как у клиента (если запись
    /// продажи её несёт). Сервер Comeback принимает пакет и без открытой лавки, вдали от NPC (проверено в игре).
    /// </summary>
    public CallResult NpcSell(IReadOnlyList<(InventoryItem Item, int Count)> items)
    {
        var entry = (int)_profile.Packets.SellEntrySize;
        if (entry is not (SellEntryWithPrice or SellEntryNoPrice))
            return CallResult.Refused("продать: устройство пакета продажи не найдено (packets.sellEntrySize)");
        if (items.Count == 0)
            return CallResult.Refused("продать: пустой список");
        foreach (var (item, count) in items)
        {
            if (count <= 0 || count > item.Count)
                return CallResult.Refused($"продать: в ячейке {item.Slot} нет {count} шт.");
            if (entry == SellEntryWithPrice && item.Price <= 0)
                return CallResult.Refused($"продать: цена предмета в ячейке {item.Slot} неизвестна");
        }

        var packet = SellPacket(items, entry);
        return Call(GameFunctions.SendPacket, packet, ("size", (uint)packet.Length));
    }

    /// <summary>
    /// Пакет продажи, как у клиента: [0x25:2][услуга 2:4][длина данных = 4 + запись·N:4][N:4], дальше N записей по 4 байта:
    /// tid, ячейка сумки, сколько и — если запись 0x10 — цена одной штуки.
    /// </summary>
    public static byte[] SellPacket(IReadOnlyList<(InventoryItem Item, int Count)> items, int entry = SellEntryWithPrice)
    {
        var packet = new byte[14 + entry * items.Count];
        void Put(int offset, uint value) => Buffer.BlockCopy(BitConverter.GetBytes(value), 0, packet, offset, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(NpcServiceCommand), 0, packet, 0, 2);
        Put(2, NpcServiceSell);
        Put(6, (uint)(4 + entry * items.Count));
        Put(10, (uint)items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var (item, count) = items[i];
            var at = 14 + entry * i;
            Put(at, item.Tid);
            Put(at + 4, (uint)item.Slot);
            Put(at + 8, (uint)count);
            if (entry == SellEntryWithPrice)
                Put(at + 12, (uint)item.Price);
        }

        return packet;
    }

    /// <summary>
    /// Починить всё в открытой лавке, как кнопкой «Починить все» с «Да»: команда ремонта с предметом -1 (так её шлёт клиент).
    /// Чинить нечего или не хватает денег — откажет сервер.
    /// </summary>
    public CallResult RepairAll() => Call(GameFunctions.RepairItem, null, ("where", 0), ("slot", 0), ("tid", 0xFFFFFFFF));

    /// <summary>Призвать пета из клетки 1..N (N — число клеток в профиле).</summary>
    public CallResult SummonPet(int cage)
    {
        var cages = _profile.PetManager.CageCount;
        return cage < 1 || cage > cages
            ? CallResult.Refused($"клетка {cage} вне 1..{cages}")
            : Call(GameFunctions.SummonPet, null, ("index", (uint)(cage - 1)));
    }

    /// <summary>Отозвать призванного пета в клетку (c2s 0x65).</summary>
    public CallResult RecallPet() => Call(GameFunctions.RecallPet, null);

    /// <summary>Скилл пакетом: цель targetWid, 0 — без цели (воскрешение пета).</summary>
    public CallResult CastSkill(int skillId, uint targetWid)
        => Call(GameFunctions.CastSkill, BitConverter.GetBytes(targetWid), ("skill", (uint)skillId), ("pvpMask", 0), ("count", targetWid != 0 ? 1u : 0u));

    /// <summary>
    /// Отменить текущее действие персонажа (каст, копание) — как Esc — и идущий макрос: кончился каст, даже отменённый, — игра
    /// сама жмёт следующий шаг макроса. Макрос не остановился (нет функции в клиенте) — действие всё равно отменяем.
    /// </summary>
    public CallResult CancelAction()
    {
        Call(GameFunctions.HostClearComboSkill, null);
        return Call(GameFunctions.CancelAction, null);
    }

    /// <summary>Приказ пету атаковать цель (данные — 1 байт pvpMask = 0).</summary>
    public CallResult PetAttack(uint targetWid)
        => Call(GameFunctions.PetCtrl, [0], ("target", targetWid), ("command", PetCommandAttack), ("size", 1));

    /// <summary>Скилл как нажатием кнопки: клиент сам подходит на дальность. target 0 — текущая цель.</summary>
    public CallResult ApplySkill(int skillId, uint targetWid)
        => Call(GameFunctions.HostApplySkill, null, ("skill", (uint)skillId), ("fromMacro", 0), ("target", targetWid), ("pvp", 0xFFFFFFFF));

    /// <summary>Запустить макрос 1..N, как кнопкой на панели: дальше игра крутит его сама. Запуск поверх идущего начинает его сначала.</summary>
    public CallResult ApplyMacro(int macro)
    {
        var count = _profile.Macros.Count;
        return macro < 1 || macro > count
            ? CallResult.Refused($"макрос {macro} вне 1..{count}")
            : Call(GameFunctions.HostApplyComboSkill, null, ("group", (uint)(macro - 1)));
    }

    /// <summary>Подобрать как кликом мыши: клиент подводит персонажа и сам отправляет подбор.</summary>
    public CallResult PickupObject(GroundItem item) => PickupObject(item.Id, gather: false);

    /// <summary>Собрать ресурс (трава, руда) как кликом мыши: PickupObject с gather — клиент подходит и копает сам.</summary>
    public CallResult Gather(GroundItem resource) => PickupObject(resource.Id, gather: true);

    private CallResult PickupObject(uint id, bool gather)
        => Call(GameFunctions.HostPickupObject, null, ("id", id), ("gather", gather ? 1u : 0u));

    /// <summary>
    /// Кнопка «Полёт» (CECHostPlayer::CmdFly, this = перс): на земле — взлететь, в воздухе — сесть. Клиент сам проверяет,
    /// что полётник надет и сейчас можно; сидит — сначала встаёт (и тогда не взлетает).
    /// </summary>
    public CallResult ToggleFly() => Call(GameFunctions.HostFly, null, ("force", 0));

    /// <summary>Лететь в точку вместе с её высотой (тип точки <see cref="MoveTypes.Fly"/>). Только в воздухе.</summary>
    public CallResult FlyTo(Position point)
        => Capabilities.WhyNot(Capability.FlyTo) is { } why ? CallResult.Refused(why) : MoveTo(point, _profile.MoveTypes.Fly);

    /// <summary>
    /// Идти в точку: по прямой, как кликом по земле, или <paramref name="smart"/> — с автопутём, как кликом по карте (если он есть
    /// у сервера, иначе по прямой). Тип точки — из <see cref="MoveTypes"/> (2 — направление, бежит бесконечно, не использовать).
    /// </summary>
    public CallResult MoveTo(Position point, bool smart)
        => MoveTo(point, smart && Capabilities.Has(Capability.SmartMove) ? _profile.MoveTypes.Smart : _profile.MoveTypes.Direct);

    // Три вызова одной заглушкой: work = WorkMan->CreateWork(1); work->SetDestination(…); WorkMan->StartWork(…)
    private CallResult MoveTo(Position point, uint type)
    {
        var names = new[] { GameFunctions.WorkCreate, GameFunctions.WorkMoveSetDestination, GameFunctions.WorkStart };
        foreach (var name in names)
        {
            if (Check(name) is { } refused)
                return refused;
        }

        if (!_roots.TryHost(out var host))
            return CallResult.Refused("персонаж не в мире");
        if (_profile.Host.WorkMan == 0 || !_memory.TryReadUInt32(host + _profile.Host.WorkMan, out var workMan) || workMan == 0)
            return CallResult.Refused("нет менеджера работ персонажа");

        var data = new byte[12];
        Buffer.BlockCopy(BitConverter.GetBytes(point.X), 0, data, 0, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(point.Height), 0, data, 4, 4);
        Buffer.BlockCopy(BitConverter.GetBytes(point.Y), 0, data, 8, 4);
        var destination = _prepared[GameFunctions.WorkMoveSetDestination];
        var start = _prepared[GameFunctions.WorkStart];
        var values = new Dictionary<string, uint> { [TypeArg] = type };
        return Result(_runner.Run(data, address =>
        {
            // Созданную работу в StartWork подставляет сама заглушка — на её месте null
            uint? Slot(Arg arg) => arg.Name == WorkArg ? null : Value(arg, values, address);
            return StubBuilder.MoveTo(workMan, _functions[names[0]].Address, _functions[names[1]].Address,
                destination.Args.Select(a => Value(a, values, address)).ToArray(), _functions[names[2]].Address, start.Args.Select(Slot).ToArray());
        }));
    }

    /// <summary>
    /// Подойти к NPC и заговорить, как вторым кликом по нему: игра сама ведёт перса по прямой до расстояния разговора и начинает
    /// разговор (окно NPC). Работа «подойти» с причиной «действовать с целью».
    /// </summary>
    public CallResult TalkTo(uint wid)
    {
        var names = new[] { GameFunctions.WorkCreate, GameFunctions.WorkTraceTarget, GameFunctions.WorkTraceSetTarget, GameFunctions.WorkStart };
        foreach (var name in names)
        {
            if (Check(name) is { } refused)
                return refused;
        }

        if (!_roots.TryHost(out var host))
            return CallResult.Refused("персонаж не в мире");
        if (_profile.Host.WorkMan == 0 || !_memory.TryReadUInt32(host + _profile.Host.WorkMan, out var workMan) || workMan == 0)
            return CallResult.Refused("нет менеджера работ персонажа");

        var start = _prepared[GameFunctions.WorkStart];
        var values = new Dictionary<string, uint>();
        return Result(_runner.Run(null, address =>
        {
            uint? Slot(Arg arg) => arg.Name == WorkArg ? null : Value(arg, values, address);
            return StubBuilder.TraceTo(workMan, _functions[names[0]].Address, _functions[names[1]].Address, wid, TraceInteract,
                _functions[names[2]].Address, _functions[names[3]].Address, start.Args.Select(Slot).ToArray());
        }));
    }

    // ── Разбор профиля и вызов ───────────────────────────────────────────────

    // Один раз: что бот даёт функции и совпадает ли это с тем, что просит профиль
    private static Prepared Prepare(string name, GameFunction definition)
    {
        // Вызывается не сама по себе (WorkCreate — внутри заглушки «идти»): разбирать нечего
        if (!Defaults.TryGetValue(name, out var defaults))
            return new Prepared(definition, [], null, null, null);

        var given = new HashSet<string>(defaults.Where(a => !TryParseNumber(a, out _)));
        string? problem = null;
        Arg Parse(string text)
        {
            if (given.Contains(text))
                return new Arg(text, 0);
            // «size+0xA9» — значение бота плюс число
            var plus = text.IndexOf('+');
            if (plus > 0 && given.Contains(text.Substring(0, plus)) && TryParseNumber(text.Substring(plus + 1), out var add))
                return new Arg(text.Substring(0, plus), add);
            if (TryParseNumber(text, out var number))
                return new Arg(null, number);
            problem ??= $"в профиле аргумент «{text}», а бот его не даёт";
            return default;
        }

        var args = (definition.Args ?? (IReadOnlyList<string>)defaults).Select(Parse).ToList();
        var registers = definition.Registers ?? new Dictionary<string, string>();
        Arg? ecx = registers.TryGetValue("ecx", out var e) ? Parse(e) : null;
        Arg? edx = registers.TryGetValue("edx", out var d) ? Parse(d) : null;
        if (registers.Keys.FirstOrDefault(r => r is not ("ecx" or "edx")) is { } unknown)
            problem ??= $"в профиле регистр «{unknown}», умеем только ecx и edx";
        if (definition.Convention == CallingConvention.Thiscall && ecx is not null)
            problem ??= "у thiscall в ecx уже лежит объект";
        if (definition.Convention == CallingConvention.Thiscall && definition.This == FunctionThis.None && !OwnThis.Contains(name))
            problem ??= "thiscall, а в профиле не сказано, чей это метод (this)";
        return new Prepared(definition, args, ecx, edx, problem);
    }

    // Имя без числа — значение как есть; «имя+число» — значение плюс число (Number); без имени — само число
    private static uint Value(Arg arg, IReadOnlyDictionary<string, uint> values, uint data)
        => arg.Name is null ? arg.Number : (arg.Name == DataArg ? data : values[arg.Name]) + arg.Number;

    /// <param name="values">Значения, которые бот даёт этой функции, по именам (все имена из <see cref="Defaults"/>).</param>
    private CallResult Call(string name, byte[]? data, params (string Name, uint Value)[] values)
    {
        if (Check(name) is { } refused)
            return refused;

        var prepared = _prepared[name];
        // this — свежий на каждом вызове (после перезахода персонаж уже другой объект)
        var thisPointer = 0u;
        if (prepared.Definition.This == FunctionThis.Host && !_roots.TryHost(out thisPointer))
            return CallResult.Refused($"{name}: персонаж не в мире");
        if (prepared.Definition.This == FunctionThis.Session && !_roots.TrySession(out thisPointer))
            return CallResult.Refused($"{name}: нет связи с сервером");
        if (prepared.Definition.This == FunctionThis.NpcDialog && !_roots.TryNpcDialog(out thisPointer))
            return CallResult.Refused($"{name}: окно NPC не найдено (ui в профиле)");
        if (prepared.Definition.This == FunctionThis.Quests && !TryQuests(out thisPointer))
            return CallResult.Refused($"{name}: объект заданий персонажа не найден (host.quests)");
        if (prepared.Definition.This != FunctionThis.None)
            thisPointer += prepared.Definition.ThisOffset;

        var known = values.ToDictionary(v => v.Name, v => v.Value);
        var address = _functions[name].Address;
        return Result(_runner.Run(data, dataAddress => StubBuilder.Call(address, prepared.Definition.Convention, thisPointer,
            prepared.Args.Select(a => Value(a, known, dataAddress)).ToArray(),
            prepared.Ecx is { } ecx ? Value(ecx, known, dataAddress) : null,
            prepared.Edx is { } edx ? Value(edx, known, dataAddress) : null)));
    }

    // Объект заданий персонажа: [перс + host.quests]
    private bool TryQuests(out uint quests)
    {
        quests = 0;
        return _profile.Host.Quests != 0 && _roots.TryHost(out var host) && _memory.TryReadUInt32(host + _profile.Host.Quests, out quests)
               && quests != 0;
    }

    private static bool TryParseNumber(string text, out uint value)
        => text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
            : uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

    /// <summary>null — можно вызывать; иначе причина отказа.</summary>
    private CallResult? Check(string name)
    {
        if (!_functions.TryGetValue(name, out var function) || !function.IsUsable)
            return CallResult.Refused($"{name}: функция не найдена в клиенте ({function?.Details ?? "нет в профиле"})");
        if (_prepared[name].Problem is { } problem)
            return CallResult.Refused($"{name}: {problem}");

        // Сигнатура — перед каждым вызовом: клиент мог обновиться или в памяти оказался чужой код
        var signature = _profile.Functions[name].Signature!;
        var actual = new byte[signature.Length];
        if (!_memory.TryRead(function.Address, actual, actual.Length) || !signature.Matches(actual))
            return CallResult.Refused($"{name}: по адресу 0x{function.Address:X8} не те байты — вызов отменён");

        return null;
    }

    private static CallResult Result(RemoteRunResult run) => run.IsDone ? CallResult.Done : CallResult.Refused(run.Details);
}

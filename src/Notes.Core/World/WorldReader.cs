using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Core.World;

/// <summary>Персонаж не в мире: экран выбора персонажа, загрузка, клиент закрывается.</summary>
public sealed class WorldNotReadyException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Читает <see cref="WorldState"/> из памяти клиента по смещениям профиля сервера. Только чтение.
/// Объект, пропавший посреди чтения (моб умер и удалён), просто не попадает в снимок.
/// </summary>
public sealed class WorldReader
{
    // Узел хэш-таблицы мира: +0x0 следующий узел в той же ячейке
    private const uint NextNode = 0x0;
    private const int MaxChain = 64;
    private const int MaxInventory = 256;
    // Больше в списке принятых не бывает (в окне «Активные N/60», с родителями — до 2×); и мобов/предметов в цели
    private const int MaxQuests = 128;
    private const int MaxQuestTargets = 8;
    private static readonly System.Text.RegularExpressions.Regex Color = new(@"\^[0-9A-Fa-f]{6}");
    private const int MaxSkills = 256;
    private const int MaxAttacks = 64;
    // Скилл по площади задевает многих — больше в снимок не берём
    private const int MaxAttackTargets = 32;
    // Окно чата держит до 200 сообщений; больше не читаем, даже если список прочитался криво
    private const int MaxChat = 256;
    private const int MaxChatText = 512;

    private readonly IMemory _memory;
    private readonly GameRoots _roots;
    private readonly ProfileData _p;
    private readonly Func<int, string?> _skillName;
    private readonly int _hostSize;
    private readonly int _npcSize;
    private readonly int _itemSize;
    private readonly int _playerSize;
    private readonly int _attackSize;
    private readonly Dictionary<uint, MineInfo?> _mines = [];
    private readonly Dictionary<uint, string?> _groups = [];
    private readonly Dictionary<uint, PetEssence> _petEssences = [];
    private readonly Dictionary<uint, (int, string?)> _foods = [];
    private readonly Dictionary<uint, (string? Name, string? Group)> _itemInfo = [];
    private readonly Dictionary<uint, (bool Aggressive, int Radius)> _monsters = [];
    // Текст сообщения чата не меняется — у каждого номера читаем один раз
    private Dictionary<int, ChatMessage> _chat = [];

    // Где персонаж (MOVEENV_* клиента): 0 — земля, 1 — вода, 2 — воздух
    private const int MoveEnvWater = 1;
    private const int MoveEnvAir = 2;

    public WorldReader(IMemory memory, GameRoots roots, ProfileData profile, Func<int, string?>? skillName = null)
    {
        _memory = memory;
        _roots = roots;
        _p = profile;
        _skillName = skillName ?? (_ => null);

        var h = profile.Host;
        _hostSize = BlockSize(h.NamePointer, h.CastFlag, h.Wid, h.Level, h.Hp, h.Mp, h.MaxHp, h.MaxMp, h.TargetId, h.PetFoodCooldown,
            h.Location + 8, h.Inventory, h.Bank, h.Skills, h.SkillsCount, h.PetManager, h.GatherTarget, h.WorkMan, h.GatherElapsed, h.GatherTotal, h.CastingSkill, h.MoveEnv);
        var n = profile.Npc;
        // Расстояние из памяти не читаем — считаем из координат (WorldState); его смещение нужно только сверке в Probe selftest
        _npcSize = BlockSize(n.Wid, n.Type, n.State, n.Level, n.Hp, n.Target, n.CastTarget, n.AttackTarget, n.PetTarget, n.NamePointer, n.Location + 8, n.Essence,
            n.Returning);
        var g = profile.GroundItem;
        _itemSize = BlockSize(g.Id, g.Tid, g.Kind, g.NamePointer, g.Location + 8);
        var pl = profile.Player;
        _playerSize = BlockSize(pl.Wid, pl.NamePointer, pl.Location + 8, pl.Level, pl.Hp, pl.MaxHp, pl.Target, pl.Class);
        var a = profile.Attacks;
        _attackSize = BlockSize(a.Lived, a.Attacker, a.Skill, a.Targets, a.TargetCount);
    }

    /// <summary>Что читать сверх обычного (банк) — просит мозг.</summary>
    public WorldRequests Requests { get; } = new();

    public WorldState Read()
    {
        var watch = Stopwatch.StartNew();
        // Всемирное время: интервалы не ломает перевод часов (синхронизация, ручная правка), в общий файл ресурсов пишется
        // одинаково из всех копий бота; в местное — только для показа
        var time = DateTime.UtcNow;

        var game = GameAddress();
        var host = ReadHost(game, out var hostBlock);
        var world = _memory.ReadUInt32(game + _p.World.World);
        var w = _p.World;
        var npcs = ReadList(world, w.Npcs, _npcSize, ReadNpc, out var npcCount);
        var items = ReadList(world, w.GroundItems, _itemSize, ReadGroundItem, out var itemCount);
        var players = ReadList(world, w.Players, _playerSize, ReadPlayer, out var playerCount);
        var inventory = ReadInventory(Field(hostBlock, _p.Host.Inventory), out var slots);
        var skills = ReadSkills(Field(hostBlock, _p.Host.Skills), (int)Field(hostBlock, _p.Host.SkillsCount));
        var pet = ReadPet(Field(hostBlock, _p.Host.PetManager));
        var bankSlots = 0;
        var bank = Requests.Bank && _p.Host.Bank != 0 ? ReadInventory(Field(hostBlock, _p.Host.Bank), out bankSlots) : null;

        return new WorldState(time, watch.Elapsed, host, npcs, items, inventory, skills, pet)
        {
            NpcCountInGame = npcCount,
            GroundItemCountInGame = itemCount,
            Players = players,
            PlayerCountInGame = playerCount,
            InventorySlots = slots,
            Bank = bank,
            BankSlots = bankSlots,
            Macros = ReadMacros(),
            Attacks = ReadAttacks(world),
            Chat = ReadChat(),
        };
    }

    /// <summary>Только ник — для списка клиентов. null, если персонаж не в мире.</summary>
    public string? TryReadHostName()
    {
        try
        {
            var host = _memory.ReadUInt32(GameAddress() + _p.Host.Struct);
            return host != 0 && _memory.TryReadUInt32(host + _p.Host.NamePointer, out var name) ? ReadName(name) : null;
        }
        catch (Exception e) when (e is WorldNotReadyException or MemoryAccessException)
        {
            return null;
        }
    }

    private uint GameAddress()
    {
        try
        {
            var game = _roots.Game();
            if (game == 0)
                throw new WorldNotReadyException("Игра не загружена (game = 0)");
            return game;
        }
        catch (MemoryAccessException e)
        {
            throw new WorldNotReadyException("Не удалось найти игру в памяти клиента: " + e.Message, e);
        }
    }

    private HostState ReadHost(uint game, out MemoryBlock block)
    {
        var address = _memory.ReadUInt32(game + _p.Host.Struct);
        if (address == 0 || !MemoryBlock.TryRead(_memory, address, _hostSize, out block))
            throw new WorldNotReadyException("Персонаж не в мире (экран выбора персонажа или загрузка)");

        var h = _p.Host;
        return new HostState(
            address,
            Field(block, h.Wid),
            ReadName(block.UInt32(h.NamePointer)),
            (int)Field(block, h.Level),
            block.Int32(h.Hp),
            block.Int32(h.MaxHp),
            block.Int32(h.Mp),
            h.MaxMp == 0 ? null : block.Int32(h.MaxMp),
            ReadPosition(block, h.Location),
            Field(block, h.TargetId),
            h.CastFlag != 0 && block.Byte(h.CastFlag) != 0,
            h.PetFoodCooldown == 0 ? 0 : Math.Max(0, block.Int32(h.PetFoodCooldown)))
        {
            CastingSkillId = h.CastingSkill == 0 ? null : ReadCastingSkill(block.UInt32(h.CastingSkill)),
            Gather = h.GatherElapsed == 0 || h.GatherTotal == 0 || IsGathering(block, h) is not { } digging ? null
                : new GatherProgress(digging, block.Int32(h.GatherElapsed), block.Int32(h.GatherTotal)),
            Flying = h.MoveEnv == 0 ? null : block.Int32(h.MoveEnv) == MoveEnvAir,
            InWater = h.MoveEnv == 0 ? null : block.Int32(h.MoveEnv) == MoveEnvWater,
        };
    }

    /// <summary>Обходит хэш-таблицу менеджера мира (мобы или предметы на земле).</summary>
    private List<T> ReadList<T>(uint world, WorldListOffsets list, int objectSize, Func<MemoryBlock, T?> read, out int? countInGame)
        where T : class
    {
        var result = new List<T>();
        countInGame = null;
        if (world == 0 || list.Manager == 0 || !_memory.TryReadUInt32(world + list.Manager, out var manager) || manager == 0)
            return result;

        var w = _p.World;
        if (list.Count != 0 && _memory.TryReadUInt32(manager + list.Count, out var count))
            countInGame = (int)count;

        var slots = _memory.ReadUInt32(manager + list.SlotArray);
        if (!MemoryBlock.TryRead(_memory, slots, w.SlotCount * 4, out var slotBlock))
            return result;

        for (var i = 0; i < w.SlotCount; i++)
        {
            var node = slotBlock.UInt32((uint)i * 4);
            for (var depth = 0; node != 0 && depth < MaxChain; depth++)
            {
                if (!MemoryBlock.TryRead(_memory, node, (int)Math.Max(NextNode, w.ObjectInSlot) + 4, out var nodeBlock))
                    break;

                if (MemoryBlock.TryRead(_memory, nodeBlock.UInt32(w.ObjectInSlot), objectSize, out var obj) && read(obj) is { } value)
                    result.Add(value);

                node = nodeBlock.UInt32(NextNode);
            }
        }

        return result;
    }

    // Список ударов менеджера атак: узел +0x0 следующий, удар — с a.Event; у цели первое поле — WID
    private List<AttackEvent> ReadAttacks(uint world)
    {
        var a = _p.Attacks;
        var attacks = new List<AttackEvent>();
        if (world == 0 || a.Manager == 0 || !_memory.TryReadUInt32(world + a.Manager, out var manager) || manager == 0
            || !_memory.TryReadUInt32(manager + a.Head, out var node))
            return attacks;

        for (var i = 0; node != 0 && i < MaxAttacks; i++)
        {
            if (MemoryBlock.TryRead(_memory, node + a.Event, _attackSize, out var e))
            {
                var hits = new List<AttackHit>();
                var count = Math.Min(e.Int32(a.TargetCount), MaxAttackTargets);
                if (count > 0 && MemoryBlock.TryRead(_memory, e.UInt32(a.Targets), count * (int)a.TargetSize, out var targets))
                {
                    for (var t = 0u; t < count; t++)
                        hits.Add(new AttackHit(targets.UInt32(t * a.TargetSize), targets.Int32(t * a.TargetSize + a.TargetDamage),
                            targets.UInt32(t * a.TargetSize + a.TargetFlags)));
                }

                var skill = e.Int32(a.Skill);
                attacks.Add(new AttackEvent(node, e.Int32(a.Lived), e.UInt32(a.Attacker), skill, hits) { SkillName = skill == 0 ? null : _skillName(skill) });
            }

            if (!_memory.TryReadUInt32(node + NextNode, out node))
                break;
        }

        return attacks;
    }

    private NpcInfo? ReadNpc(MemoryBlock b)
    {
        var n = _p.Npc;
        var state = (NpcState)Field(b, n.State);
        var kind = (NpcKind)b.Int32(n.Type);

        // Кто атакован: обычное поле, а если оно пусто (0 или -1) — цель удара у бьющего, цель каста у кастующего,
        // у пета — на кого он идёт
        var target = Field(b, n.Target);
        if (target is 0 or uint.MaxValue)
        {
            target = state switch
            {
                NpcState.Attacking when n.AttackTarget != 0 => Field(b, n.AttackTarget),
                NpcState.Casting when n.CastTarget != 0 => Field(b, n.CastTarget),
                _ when kind == NpcKind.Pet && n.PetTarget != 0 => Field(b, n.PetTarget),
                _ => target,
            };
        }

        // Только факты из памяти: цель как есть (застрявшую после отагра и «идёт к нам» толкует NpcTracker — нужна история)
        var npc = new NpcInfo(
            b.Address,
            b.UInt32(n.Wid),
            kind,
            state,
            target,
            ReadPosition(b, n.Location),
            ReadName(b.UInt32(n.NamePointer)),
            (int)Field(b, n.Hp))
        {
            Level = (int)Field(b, n.Level),
            Returning = n.Returning != 0 && (b.UInt32(n.Returning) & n.ReturningFlag) != 0,
        };
        return n.Essence != 0 && ReadMonster(b.UInt32(n.Essence)) is { } m
            ? npc with { Aggressive = m.Aggressive, AggroRadius = m.Radius }
            : npc;
    }

    // Запись моба в справочнике не меняется, пока клиент запущен, — у каждой читаем один раз
    private (bool Aggressive, int Radius)? ReadMonster(uint record)
    {
        if (record == 0)
            return null;
        if (_monsters.TryGetValue(record, out var known))
            return known;

        var m = _p.MonsterEssence;
        if (!_memory.TryReadUInt32(record + m.Aggressive, out var aggressive) || !_memory.TryReadUInt32(record + m.AggroRadius, out var radius))
            return null;
        return _monsters[record] = (aggressive != 0, unchecked((int)radius));
    }

    // Список чата: [список] — начало массива записей, [список + 4] — конец
    private IReadOnlyList<ChatMessage> ReadChat()
    {
        var c = _p.Chat;
        if (c.EntrySize == 0 || _roots.ChatList() is not { } list || !_memory.TryReadUInt32(list, out var begin)
            || !_memory.TryReadUInt32(list + 4, out var end) || end < begin)
            return [];

        var count = (int)Math.Min((end - begin) / c.EntrySize, MaxChat);
        if (count == 0 || !MemoryBlock.TryRead(_memory, begin, count * (int)c.EntrySize, out var block))
            return [];

        var messages = new List<ChatMessage>(count);
        var known = new Dictionary<int, ChatMessage>(count);
        for (var i = 0; i < count; i++)
        {
            var at = (uint)i * c.EntrySize;
            var number = block.Int32(at + c.Number);
            if (!_chat.TryGetValue(number, out var message))
            {
                var text = block.UInt32(at + c.Text);
                message = new ChatMessage(number, block.Byte(at + c.Channel), ReadName(text, MaxChatText),
                    block.UInt32(at + c.Sender));
            }

            messages.Add(message);
            known[number] = message;
        }

        _chat = known;
        return messages;
    }

    private PlayerInfo? ReadPlayer(MemoryBlock b)
    {
        var p = _p.Player;
        return new PlayerInfo(b.Address, b.UInt32(p.Wid), ReadName(b.UInt32(p.NamePointer)), ReadPosition(b, p.Location))
        {
            Level = (int)Field(b, p.Level),
            Hp = (int)Field(b, p.Hp),
            MaxHp = (int)Field(b, p.MaxHp),
            TargetWid = Field(b, p.Target),
            Class = p.Class == 0 ? null : b.Int32(p.Class),
        };
    }

    private GroundItem? ReadGroundItem(MemoryBlock b)
    {
        var g = _p.GroundItem;
        var name = b.UInt32(g.NamePointer);
        var item = new GroundItem(
            b.Address,
            b.UInt32(g.Id),
            b.UInt32(g.Tid),
            (GroundItemKind)b.Byte(g.Kind),
            ReadPosition(b, g.Location),
            ReadName(name));
        if (item.Kind == GroundItemKind.Item)
            return item with { Group = ReadGroup(item.Tid, name) };
        if (item.Kind != GroundItemKind.Resource)
            return item;

        var mine = ReadMine(item.Tid, name);
        return item with { Mine = mine, Special = !IsRegular(mine) };
    }

    // Обычный ресурс — копается нашим инструментом (киркой) без квеста. Остальное (квестовые трупы, ящики, печати, особые
    // инструменты) — «нересурс». Справочник не прочитался — считаем обычным
    private bool IsRegular(MineInfo? mine)
        => mine is null || _p.GatherTools.Count == 0 || (mine.Quest == 0 && _p.GatherTools.Contains(mine.Tool));

    // Записи справочника не меняются, пока клиент запущен, — по tid читаем один раз
    private MineInfo? ReadMine(uint tid, uint namePointer)
    {
        var m = _p.MineEssence;
        if (m.Size == 0 || namePointer < m.NameInRecord)
            return null;
        if (_mines.TryGetValue(tid, out var known))
            return known;
        if (!MemoryBlock.TryRead(_memory, namePointer - m.NameInRecord, (int)m.Size, out var r))
            return null;

        MineInfo? mine = null;
        if (r.UInt32(m.Id) == tid)
        {
            // Сколько за копку: два варианта {число, вероятность}; берём самый большой из возможных
            var most = Math.Max(r.Float(m.Amounts + 4) > 0 ? r.Int32(m.Amounts) : 0, r.Float(m.Amounts + 12) > 0 ? r.Int32(m.Amounts + 8) : 0);
            var yields = new Dictionary<uint, int>();
            for (var i = 0u; i < m.MaterialSlots; i++)
            {
                var material = r.UInt32(m.Materials + i * 8);
                if (material != 0 && r.Float(m.Materials + i * 8 + 4) > 0)
                    yields[material] = Math.Max(most, 1);
            }

            mine = new MineInfo(r.UInt32(m.Tool), r.UInt32(m.Quest), yields)
            {
                LevelRequired = m.LevelRequired == 0 ? 0 : r.Int32(m.LevelRequired),
            };
        }

        _mines[tid] = mine;
        return mine;
    }

    // Группа лута — по типу из записи предмета; запись не меняется, пока клиент запущен, — по tid читаем один раз
    private string? ReadGroup(uint tid, uint namePointer)
    {
        if (_p.LootGroups.Count == 0)
            return null;
        if (_groups.TryGetValue(tid, out var known))
            return known;
        // Списком tid (корм пета: типа в его записи нет) — память не читаем
        if (_p.LootGroups.FirstOrDefault(g => g.Tids.Contains(tid)) is { } listed)
            return _groups[tid] = listed.Name;

        string? group = null;
        var unread = false;
        foreach (var e in _p.ItemEssences)
        {
            if (e.NameInRecord == 0 || namePointer < e.NameInRecord)
                continue;
            var record = namePointer - e.NameInRecord;
            if (!_memory.TryReadUInt32(record + e.Id, out var id) || !_memory.TryReadUInt32(record + e.Type, out var type))
            {
                unread = true;
                continue;
            }

            // Раскладка не та (или у предмета нет типа) — tid на месте не совпадёт
            if (id != tid)
                continue;
            group = _p.LootGroups.FirstOrDefault(g => g.Types.Contains(type))?.Name;
            break;
        }

        // Не прочиталось — в следующем снимке попробуем ещё
        if (group is not null || !unread)
            _groups[tid] = group;
        return group;
    }

    /// <summary>
    /// Основная вкладка банка. Не входит в снимок мира: нужна по требованию (перед переносом предметов), а не четыре раза в
    /// секунду. null — банка в профиле нет; список пуст — клиент ещё не получил его от сервера (банк открывают первый раз за вход).
    /// </summary>
    public IReadOnlyList<InventoryItem>? ReadBank(out int slots)
    {
        slots = 0;
        if (_p.Host.Bank == 0)
            return null;

        ReadHost(GameAddress(), out var hostBlock);
        return ReadInventory(Field(hostBlock, _p.Host.Bank), out slots);
    }

    /// <summary>
    /// Принятые задания, по порядку списка игры (родитель раньше своих подзаданий). Не входит в снимок мира — по требованию.
    /// null — заданий в профиле нет.
    /// </summary>
    public IReadOnlyList<QuestInfo>? ReadQuests()
    {
        var q = _p.Quests;
        if (_p.Host.Quests == 0 || q.EntrySize == 0)
            return null;

        var result = new List<QuestInfo>();
        ReadHost(GameAddress(), out var hostBlock);
        var book = Field(hostBlock, _p.Host.Quests);
        if (book == 0 || !_memory.TryReadUInt32(book + q.Active, out var list) || !MemoryBlock.TryRead(_memory, list, 1, out var head))
            return result;

        var count = Math.Min((int)head.Byte(0), MaxQuests);
        if (count == 0 || !MemoryBlock.TryRead(_memory, list + q.Entries, count * (int)q.EntrySize, out var entries))
            return result;

        var ids = new int[count];
        for (var i = 0; i < count; i++)
            ids[i] = entries.UInt16((uint)i * q.EntrySize + q.EntryId);

        var templateSize = BlockSize(q.Id, q.Goal, q.AwardNpc, q.MonsterCount, q.Monsters, q.ItemCount, q.Items);
        for (var i = 0; i < count; i++)
        {
            var entry = (uint)i * q.EntrySize;
            var address = entries.UInt32(entry + q.EntryTemplate);
            // Запись не та (задание сдано, место в списке занято другим) — номер в ней не совпадёт
            if (!MemoryBlock.TryRead(_memory, address, templateSize, out var t) || t.Int32(q.Id) != ids[i])
                continue;

            var parent = entries.Byte(entry + q.EntryParent);
            var goal = t.Int32(q.Goal);
            var monsters = ReadQuestTargets(t.Int32(q.MonsterCount), t.UInt32(q.Monsters), q.MonsterSize, 4,
                k => goal == QuestGoals.Kill ? entries.UInt16(entry + q.EntryKills + (uint)k * 2) : null);
            var items = ReadQuestTargets(t.Int32(q.ItemCount), t.UInt32(q.Items), q.ItemSize, q.ItemNeed, _ => null);
            var name = Color.Replace(_memory.ReadUnicodeString(address + q.Name, 32), "");
            result.Add(new QuestInfo(ids[i], name, parent < count ? ids[parent] : 0, goal, Field(t, q.AwardNpc), monsters, items));
        }

        return result;
    }

    private List<QuestTarget> ReadQuestTargets(int count, uint array, uint size, uint need, Func<int, int?> done)
    {
        var result = new List<QuestTarget>();
        count = Math.Min(count, MaxQuestTargets);
        if (count <= 0 || size == 0 || !MemoryBlock.TryRead(_memory, array, count * (int)size, out var block))
            return result;

        for (var k = 0; k < count; k++)
        {
            var at = (uint)k * size;
            result.Add(new QuestTarget(block.UInt32(at), block.Int32(at + need), done(k)));
        }

        return result;
    }

    private List<InventoryItem> ReadInventory(uint inventory, out int slots)
    {
        var result = new List<InventoryItem>();
        slots = 0;
        var inv = _p.Inventory;
        if (inventory == 0 || !MemoryBlock.TryRead(_memory, inventory, (int)Math.Max(inv.Items, inv.Size) + 4, out var bag))
            return result;

        var size = Math.Min(bag.Int32(inv.Size), MaxInventory);
        slots = Math.Max(size, 0);
        if (size <= 0 || !MemoryBlock.TryRead(_memory, bag.UInt32(inv.Items), size * 4, out var cells))
            return result;

        var itemSize = BlockSize(inv.ItemCategory, inv.ItemTid, inv.ItemCount, inv.ItemMaxCount, inv.ItemEssence, inv.FoodEssence);
        for (var slot = 0; slot < size; slot++)
        {
            var address = cells.UInt32((uint)slot * 4);
            if (!MemoryBlock.TryRead(_memory, address, itemSize, out var item))
                continue;

            var category = item.Int32(inv.ItemCategory);
            var tid = item.UInt32(inv.ItemTid);
            PotionInfo? potion = null;
            (int Loyalty, string? Name)? food = null;
            if (category == inv.CategoryPotion)
                potion = ReadPotion(item.UInt32(inv.ItemEssence));
            else if (category == inv.CategoryPetFood)
                food = ReadFood(tid, item.UInt32(inv.FoodEssence));

            var info = ReadItemInfo(tid, address);
            result.Add(new InventoryItem(slot, tid, category, item.Int32(inv.ItemCount), potion, food?.Loyalty)
            {
                MaxCount = inv.ItemMaxCount == 0 ? 0 : item.Int32(inv.ItemMaxCount),
                Name = food?.Name ?? info.Name,
                Group = info.Group,
            });
        }

        return result;
    }

    // Название и группа предмета сумки или банка. Описание — запись справочника, на которую указывает предмет (+0x0 — его tid);
    // где лежит указатель, у разных классов предметов разное, поэтому ищем его в начале предмета. Запись не меняется, пока клиент
    // запущен, — по tid ищем один раз (не прочли предмет — попробуем в другой раз)
    private (string? Name, string? Group) ReadItemInfo(uint tid, uint item)
    {
        var search = (int)_p.Inventory.EssenceSearch;
        if (search == 0)
            return default;
        if (_itemInfo.TryGetValue(tid, out var known))
            return known;
        if (!MemoryBlock.TryRead(_memory, item, search, out var b) && !MemoryBlock.TryRead(_memory, item, search /= 2, out b))
            return default;

        var listed = _p.LootGroups.FirstOrDefault(g => g.Tids.Contains(tid))?.Name;
        for (var offset = 0u; offset + 4 <= search; offset += 4)
        {
            var record = b.UInt32(offset);
            if (record < 0x10000 || record == item || !_memory.TryReadUInt32(record, out var id) || id != tid)
                continue;
            if (RecordName(record, out var essence) is not { } name)
                continue;

            var group = listed;
            if (group is null && essence is not null && _memory.TryReadUInt32(record + essence.Type, out var type))
                group = _p.LootGroups.FirstOrDefault(g => g.Types.Contains(type))?.Name;
            return _itemInfo[tid] = (name, group);
        }

        return _itemInfo[tid] = (null, listed);
    }

    // Название в записи справочника. Раскладки с типом (itemEssences) — название сразу за числом (тип, подтип), поэтому перед ним
    // два нулевых байта (старшие байты числа); у записи без типа название — сразу за tid. Пробуем с самого дальнего места: ближе
    // попадаются хвост названия или тип, прочитанный как буква
    private string? RecordName(uint record, out ItemEssenceOffsets? essence)
    {
        foreach (var e in _p.ItemEssences.Where(e => e.NameInRecord >= 2).OrderByDescending(e => e.NameInRecord))
        {
            if (_memory.TryReadUInt32(record + e.NameInRecord - 2, out var before) && (before & 0xFFFF) == 0
                && Printable(ReadName(record + e.NameInRecord, 32)) is { } typed)
            {
                essence = e;
                return typed;
            }
        }

        essence = null;
        return _p.Inventory.PlainName == 0 ? null : Printable(ReadName(record + _p.Inventory.PlainName, 32));
    }

    private static string? Printable(string text)
        => text.Trim().Length > 0 && text.All(ch => ch >= ' ' && ch != '\uFFFD') ? text.Trim() : null;

    // Описание корма в справочнике не меняется, пока клиент запущен, — по tid читаем один раз (не прочли — попробуем в другой раз)
    private (int Loyalty, string? Name)? ReadFood(uint tid, uint address)
    {
        if (_foods.TryGetValue(tid, out var known))
            return known;

        var f = _p.PetFood;
        if (address == 0 || !_memory.TryReadUInt32(address, out var id) || id != tid || !_memory.TryReadUInt32(address + f.Loyalty, out var loyalty))
            return null;
        var name = f.Name == 0 ? "" : ReadName(address + f.Name);
        return _foods[tid] = ((int)loyalty, name.Length > 0 ? name : null);
    }

    private PotionInfo? ReadPotion(uint essence)
    {
        var p = _p.Potion;
        if (!MemoryBlock.TryRead(_memory, essence, BlockSize(p.RequiredLevel, p.Hp, p.HpSeconds, p.Mp, p.MpSeconds), out var b))
            return null;

        return new PotionInfo(b.Int32(p.RequiredLevel), b.Int32(p.Hp), b.Int32(p.HpSeconds), b.Int32(p.Mp), b.Int32(p.MpSeconds));
    }

    // Номер скилла по указателю на его объект; 0 — не кастует (или объект не прочитался)
    private int ReadCastingSkill(uint skill)
        => skill != 0 && _memory.TryReadUInt32(skill + _p.Skill.Id, out var id) ? (int)id : 0;

    private List<SkillInfo> ReadSkills(uint array, int count)
    {
        var result = new List<SkillInfo>();
        count = Math.Min(count, MaxSkills);
        if (count <= 0 || !MemoryBlock.TryRead(_memory, array, count * 4, out var pointers))
            return result;

        var s = _p.Skill;
        var size = BlockSize(s.Id, s.Level, s.CooldownLeft, s.CooldownFull, s.Flags);
        for (var i = 0; i < count; i++)
        {
            if (!MemoryBlock.TryRead(_memory, pointers.UInt32((uint)i * 4), size, out var b))
                continue;

            var id = b.Int32(s.Id);
            result.Add(new SkillInfo(id, b.Int32(s.Level), b.Int32(s.CooldownLeft), b.Int32(s.CooldownFull), b.UInt32(s.Flags), _skillName(id)));
        }

        return result;
    }

    private PetState? ReadPet(uint manager)
    {
        var m = _p.PetManager;
        if (manager == 0 || m.CageCount <= 0)
            return null;
        if (!MemoryBlock.TryRead(_memory, manager, BlockSize(m.ActiveCage, m.ActivePetWid, m.Cages + (uint)(m.CageCount - 1) * 4), out var b))
            return null;

        var cages = new List<PetInCage>();
        var pet = _p.Pet;
        for (var cage = 1; cage <= m.CageCount; cage++)
        {
            var address = b.UInt32(m.Cages + (uint)(cage - 1) * 4);
            if (!MemoryBlock.TryRead(_memory, address, BlockSize(pet.HpRatio, pet.Hunger, pet.Tid, pet.Essence, pet.Level), out var petBlock))
                continue;
            var tid = petBlock.UInt32(pet.Tid);
            var essence = pet.EssenceInhabit == 0 || pet.Essence == 0 ? null : ReadPetEssence(tid, petBlock.UInt32(pet.Essence));
            var level = (int)Field(petBlock, pet.Level);
            cages.Add(new PetInCage(cage, petBlock.Float(pet.HpRatio), petBlock.Int32(pet.Hunger))
            {
                Name = essence?.Name,
                Habitat = essence?.Habitat,
                Level = level,
                MaxHp = level > 0 && essence?.Hp is { } hp ? hp.MaxHp(level) : 0,
            });
        }

        if (cages.Count == 0)
            return null;

        var active = b.Int32(m.ActiveCage);
        return new PetState(active >= 0 && active < m.CageCount ? active + 1 : null, b.UInt32(m.ActivePetWid), cages);
    }

    // Запись питомца в справочнике не меняется, пока клиент запущен, — по tid читаем один раз (не нашли — попробуем в другой раз)
    private PetEssence? ReadPetEssence(uint tid, uint address)
    {
        if (_petEssences.TryGetValue(tid, out var known))
            return known;

        var p = _p.Pet;
        if (address == 0 || !MemoryBlock.TryRead(_memory, address, BlockSize(p.EssenceInhabit, p.EssenceLevelRequire, p.EssenceHpA, p.EssenceHpB, p.EssenceHpC),
                out var b) || b.UInt32(0) != tid)
            return null;

        var hp = p.EssenceLevelRequire == 0 || p.EssenceHpA == 0 || p.EssenceHpB == 0 || p.EssenceHpC == 0
            ? null
            : new PetHpFormula(b.Int32(p.EssenceLevelRequire), b.Float(p.EssenceHpA), b.Float(p.EssenceHpB), b.Float(p.EssenceHpC));
        return _petEssences[tid] = new PetEssence(ReadName(address + p.EssenceName), PetHabitats.FromGame(b.Int32(p.EssenceInhabit)), hp);
    }

    private sealed record PetEssence(string Name, PetHabitat? Habitat, PetHpFormula? Hp);

    /// <summary>Максимум HP пета, как его считает окно пета игры: всё во float — иначе на круглых числах разойдётся на 1.</summary>
    private sealed record PetHpFormula(int LevelRequire, float A, float B, float C)
    {
        public int MaxHp(int level) => (int)(float)((float)((float)(level - (float)(LevelRequire * B)) + C) * A);
    }

    // Макросы — в настройках игры, а не у персонажа; читаем только, какие есть и их иконки
    private List<MacroInfo> ReadMacros()
    {
        var m = _p.Macros;
        var macros = new List<MacroInfo>();
        var size = (int)m.Size;
        var bytes = new byte[size * m.Count];
        if (m.List == 0 || bytes.Length == 0 || _p.Base.Configs == 0 || !_roots.TryBase(out var basePointer)
            || !_memory.TryReadUInt32(basePointer + _p.Base.Configs, out var configs) || configs == 0
            || !_memory.TryRead(configs + m.List, bytes, bytes.Length))
            return macros;

        for (var i = 0; i < m.Count; i++)
        {
            var icon = bytes[i * size];
            if (icon != 0)
                macros.Add(new MacroInfo(i + 1, icon));
        }

        return macros;
    }

    private string ReadName(uint pointer, int maxChars = 64)
    {
        if (pointer == 0)
            return "";
        try
        {
            return _memory.ReadUnicodeString(pointer, maxChars);
        }
        catch (MemoryAccessException)
        {
            return "";
        }
    }

    private static Position ReadPosition(MemoryBlock b, uint offset) => new(b.Float(offset), b.Float(offset + 4), b.Float(offset + 8));

    // Копает ли — как сама игра: по работе «сбор» или по id ресурса; null — не знаем как. Обработчик «начало сбора» обычно
    // заводит работу «сбор» и id не пишет — по одному id бот не видел копания, бросал ресурс и думал, что его выкопал другой.
    // Прежний байт «0 — копает» не годился: в воде он 0 и без копания — бот без конца «прерывал копание» и стоял
    private bool? IsGathering(MemoryBlock b, HostOffsets h)
    {
        bool? byTarget = h.GatherTarget == 0 ? null : b.UInt32(h.GatherTarget) != 0;
        return byTarget == true ? true : HasGatherWork(b, h) ?? byTarget;
    }

    // Текущая работа персонажа — «сбор»; null — в профиле нет раскладки работ
    private bool? HasGatherWork(MemoryBlock b, HostOffsets h)
    {
        var work = _p.GatherWork;
        if (work.Current == 0 || h.WorkMan == 0)
            return null;
        // Не прочиталось (работа сменилась между чтениями) — «не копает»: на следующем снимке прочитается.
        // Работы списком — пустой список значит «не копает», а в массиве может лежать старая работа
        var manager = b.UInt32(h.WorkMan);
        if (work.CurrentCount != 0 && !(_memory.TryReadUInt32(manager + work.CurrentCount, out var count) && count != 0))
            return false;
        return _memory.TryReadUInt32(manager + work.Current, out var current) && current != 0
            && (work.CurrentCount == 0 || _memory.TryReadUInt32(current, out current) && current != 0)
            && _memory.TryReadUInt32(current + work.Type, out var type) && type == work.Gather
            && _memory.TryReadUInt32(current + work.Flag, out var flag) && (flag & 0xFF) != 0;
    }

    // Смещение 0 в профиле — «поле не найдено»: читать нечего (иначе прочиталось бы начало объекта, vtable)
    private static uint Field(MemoryBlock b, uint offset) => offset == 0 ? 0 : b.UInt32(offset);

    // Размер блока, в который влезают все поля (каждое — 4 байта)
    private static int BlockSize(params uint[] offsets) => (int)offsets.Max() + 4;
}

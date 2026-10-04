using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;

namespace BotCH.Core.World;

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
    private const int MaxSkills = 256;

    private readonly IMemory _memory;
    private readonly uint _moduleBase;
    private readonly ProfileData _p;
    private readonly Func<int, string?> _skillName;
    private readonly int _hostSize;
    private readonly int _npcSize;
    private readonly int _itemSize;
    private readonly Dictionary<uint, MineInfo?> _mines = [];
    private readonly Dictionary<uint, (string?, PetHabitat?)> _petEssences = [];
    private readonly Dictionary<uint, (bool Aggressive, int Radius)> _monsters = [];

    // Где персонаж (MOVEENV_* клиента): 0 — земля, 1 — вода, 2 — воздух
    private const int MoveEnvWater = 1;
    private const int MoveEnvAir = 2;

    public WorldReader(IMemory memory, uint moduleBase, ProfileData profile, Func<int, string?>? skillName = null)
    {
        _memory = memory;
        _moduleBase = moduleBase;
        _p = profile;
        _skillName = skillName ?? (_ => null);

        var h = profile.Host;
        _hostSize = BlockSize(h.NamePointer, h.CastFlag, h.Wid, h.Level, h.Hp, h.Mp, h.MaxHp, h.MaxMp, h.TargetId, h.PetFoodCooldown,
            h.Location + 8, h.Inventory, h.Skills, h.SkillsCount, h.PetManager, h.GatherIdle, h.GatherElapsed, h.GatherTotal, h.CastingSkill, h.MoveEnv);
        var n = profile.Npc;
        // Расстояние из памяти не читаем — считаем из координат (WorldState); его смещение нужно только сверке в Probe selftest
        _npcSize = BlockSize(n.Wid, n.Type, n.State, n.Level, n.Hp, n.Target, n.CastTarget, n.AttackTarget, n.NamePointer, n.Location + 8, n.Essence,
            n.Returning);
        var g = profile.GroundItem;
        _itemSize = BlockSize(g.Id, g.Tid, g.Kind, g.NamePointer, g.Location + 8);
    }

    public WorldState Read()
    {
        var watch = Stopwatch.StartNew();
        var time = DateTime.Now;

        var game = GameAddress();
        var host = ReadHost(game, out var hostBlock);
        var world = _memory.ReadUInt32(game + _p.World.World);
        var w = _p.World;
        var npcs = ReadList(world, w.Npcs, _npcSize, ReadNpc, out var npcCount);
        var items = ReadList(world, w.GroundItems, _itemSize, ReadGroundItem, out var itemCount);
        var inventory = ReadInventory(Field(hostBlock, _p.Host.Inventory), out var slots);
        var skills = ReadSkills(Field(hostBlock, _p.Host.Skills), (int)Field(hostBlock, _p.Host.SkillsCount));
        var pet = ReadPet(Field(hostBlock, _p.Host.PetManager));

        return new WorldState(time, watch.Elapsed, host, npcs, items, inventory, skills, pet)
        {
            NpcCountInGame = npcCount,
            GroundItemCountInGame = itemCount,
            InventorySlots = slots,
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
            var game = _memory.ReadPointerChain(_moduleBase + _p.Base.BasePointer, _p.Base.Game);
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
            Gather = h.GatherIdle == 0 || h.GatherElapsed == 0 || h.GatherTotal == 0 ? null
                : new GatherProgress(block.Byte(h.GatherIdle) == 0, block.Int32(h.GatherElapsed), block.Int32(h.GatherTotal)),
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

    private NpcInfo? ReadNpc(MemoryBlock b)
    {
        var n = _p.Npc;
        var state = (NpcState)Field(b, n.State);

        // Кто атакован: обычное поле, а если оно пусто (0 или -1) — цель удара у бьющего, цель каста у кастующего
        var target = Field(b, n.Target);
        if (target is 0 or uint.MaxValue)
        {
            target = state switch
            {
                NpcState.Attacking when n.AttackTarget != 0 => Field(b, n.AttackTarget),
                NpcState.Casting when n.CastTarget != 0 => Field(b, n.CastTarget),
                _ => target,
            };
        }

        // Только факты из памяти: цель как есть (застрявшую после отагра и «идёт к нам» толкует NpcTracker — нужна история)
        var npc = new NpcInfo(
            b.Address,
            b.UInt32(n.Wid),
            (NpcKind)b.Int32(n.Type),
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

    private GroundItem? ReadGroundItem(MemoryBlock b)
    {
        var g = _p.GroundItem;
        var name = b.UInt32(g.NamePointer);
        var item = new GroundItem(
            b.Address,
            b.UInt32(g.Id),
            b.UInt32(g.Tid),
            (GroundItemKind)b.Int32(g.Kind),
            ReadPosition(b, g.Location),
            ReadName(name));
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
            if (!MemoryBlock.TryRead(_memory, cells.UInt32((uint)slot * 4), itemSize, out var item))
                continue;

            var category = item.Int32(inv.ItemCategory);
            PotionInfo? potion = null;
            int? loyalty = null;
            if (category == inv.CategoryPotion)
                potion = ReadPotion(item.UInt32(inv.ItemEssence));
            else if (category == inv.CategoryPetFood && _memory.TryReadUInt32(item.UInt32(inv.FoodEssence) + _p.PetFood.Loyalty, out var value))
                loyalty = (int)value;

            result.Add(new InventoryItem(slot, item.UInt32(inv.ItemTid), category, item.Int32(inv.ItemCount), potion, loyalty)
            {
                MaxCount = inv.ItemMaxCount == 0 ? 0 : item.Int32(inv.ItemMaxCount),
            });
        }

        return result;
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
            if (!MemoryBlock.TryRead(_memory, address, BlockSize(pet.HpRatio, pet.Hunger, pet.Tid, pet.Essence), out var petBlock))
                continue;
            var tid = petBlock.UInt32(pet.Tid);
            var (name, habitat) = pet.EssenceInhabit == 0 ? (null, null)
                : ReadPetEssence(tid, () => pet.Essence != 0 ? petBlock.UInt32(pet.Essence) : FindEssence(tid));
            cages.Add(new PetInCage(cage, petBlock.Float(pet.HpRatio), petBlock.Int32(pet.Hunger)) { Name = name, Habitat = habitat });
        }

        if (cages.Count == 0)
            return null;

        var active = b.Int32(m.ActiveCage);
        return new PetState(active >= 0 && active < m.CageCount ? active + 1 : null, b.UInt32(m.ActivePetWid), cages);
    }

    // Запись питомца в справочнике не меняется, пока клиент запущен, — по tid читаем один раз (не нашли — попробуем в другой раз)
    private (string? Name, PetHabitat? Habitat) ReadPetEssence(uint tid, Func<uint> record)
    {
        if (_petEssences.TryGetValue(tid, out var known))
            return known;

        var p = _p.Pet;
        var address = record();
        if (address == 0 || !_memory.TryReadUInt32(address, out var id) || id != tid || !_memory.TryReadUInt32(address + p.EssenceInhabit, out var inhabit))
            return (null, null);
        return _petEssences[tid] = (ReadName(address + p.EssenceName), PetHabitats.FromGame(unchecked((int)inhabit)));
    }

    // Запись-описание по id — как клиент (get_data_ptr): ячейка id % число ячеек, по цепочке узлов до своего id, дальше
    // начало таблицы вида данных + размер записи × номер. 0 — справочника нет в профиле или id не нашёлся
    private uint FindEssence(uint id)
    {
        var d = _p.ElementData;
        if (d.Manager == 0 || id == 0
            || !_memory.TryReadUInt32(_moduleBase + _p.Base.BasePointer, out var basePointer)
            || !_memory.TryReadUInt32(basePointer + d.Manager, out var manager) || manager == 0
            || !_memory.TryReadUInt32(manager + d.EssenceMap + d.MapBuckets, out var buckets)
            || !_memory.TryReadUInt32(manager + d.EssenceMap + d.MapBucketCount, out var count) || count == 0
            || !_memory.TryReadUInt32(buckets + id % count * 4, out var node))
            return 0;

        for (var depth = 0; node != 0 && depth < MaxChain; depth++)
        {
            if (!_memory.TryReadUInt32(node + d.NodeKey, out var key))
                return 0;
            if (key == id)
            {
                return _memory.TryReadUInt32(node + d.NodeType, out var type) && _memory.TryReadUInt32(node + d.NodeIndex, out var index)
                       && _memory.TryReadUInt32(manager + d.TypeBases, out var bases) && _memory.TryReadUInt32(manager + d.TypeSizes, out var sizes)
                       && _memory.TryReadUInt32(bases + type * 4, out var start) && _memory.TryReadUInt32(sizes + type * 4, out var size)
                    ? start + size * index
                    : 0;
            }

            if (!_memory.TryReadUInt32(node + NextNode, out node))
                return 0;
        }

        return 0;
    }

    private string ReadName(uint pointer)
    {
        if (pointer == 0)
            return "";
        try
        {
            return _memory.ReadUnicodeString(pointer);
        }
        catch (MemoryAccessException)
        {
            return "";
        }
    }

    private static Position ReadPosition(MemoryBlock b, uint offset) => new(b.Float(offset), b.Float(offset + 4), b.Float(offset + 8));

    // Смещение 0 в профиле — «поле не найдено»: читать нечего (иначе прочиталось бы начало объекта, vtable)
    private static uint Field(MemoryBlock b, uint offset) => offset == 0 ? 0 : b.UInt32(offset);

    // Размер блока, в который влезают все поля (каждое — 4 байта)
    private static int BlockSize(params uint[] offsets) => (int)offsets.Max() + 4;
}

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

    public WorldReader(IMemory memory, uint moduleBase, ProfileData profile, Func<int, string?>? skillName = null)
    {
        _memory = memory;
        _moduleBase = moduleBase;
        _p = profile;
        _skillName = skillName ?? (_ => null);

        var h = profile.Host;
        _hostSize = BlockSize(h.NamePointer, h.CastFlag, h.Wid, h.Level, h.Hp, h.Mp, h.MaxHp, h.MaxMp, h.TargetId, h.HpPotionCooldown,
            h.Location + 8, h.Inventory, h.Skills, h.SkillsCount, h.PetManager);
        var n = profile.Npc;
        _npcSize = BlockSize(n.Wid, n.Type, n.State, n.Hp, n.Distance, n.Target, n.NamePointer, n.Location + 8);
        var g = profile.GroundItem;
        _itemSize = BlockSize(g.Id, g.Tid, g.Kind, g.Distance, g.NamePointer, g.Location + 8);
    }

    public WorldState Read()
    {
        var watch = Stopwatch.StartNew();
        var time = DateTime.Now;

        var game = GameAddress();
        var host = ReadHost(game, out var hostBlock);
        var world = _memory.ReadUInt32(game + _p.World.World);
        var npcs = ReadList(world, _p.World.Npcs, _npcSize, ReadNpc, out var npcCount);
        var items = ReadList(world, _p.World.GroundItems, _itemSize, ReadGroundItem, out var itemCount);
        var inventory = ReadInventory(hostBlock.UInt32(_p.Host.Inventory));
        var skills = ReadSkills(hostBlock.UInt32(_p.Host.Skills), hostBlock.Int32(_p.Host.SkillsCount));
        var pet = ReadPet(hostBlock.UInt32(_p.Host.PetManager));

        return new WorldState(time, watch.Elapsed, host, npcs, items, inventory, skills, pet)
        {
            NpcCountInGame = npcCount,
            GroundItemCountInGame = itemCount,
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
            block.UInt32(h.Wid),
            ReadName(block.UInt32(h.NamePointer)),
            block.Int32(h.Level),
            block.Int32(h.Hp),
            block.Int32(h.MaxHp),
            block.Int32(h.Mp),
            h.MaxMp == 0 ? null : block.Int32(h.MaxMp),
            ReadPosition(block, h.Location),
            block.UInt32(h.TargetId),
            block.Byte(h.CastFlag) != 0,
            block.UInt32(h.HpPotionCooldown) == 0);
    }

    /// <summary>Обходит хэш-таблицу менеджера мира (мобы или предметы на земле).</summary>
    private List<T> ReadList<T>(uint world, uint managerOffset, int objectSize, Func<MemoryBlock, T?> read, out int countInGame)
        where T : class
    {
        var result = new List<T>();
        countInGame = -1;
        if (world == 0 || !_memory.TryReadUInt32(world + managerOffset, out var manager) || manager == 0)
            return result;

        var w = _p.World;
        if (w.Count != 0 && _memory.TryReadUInt32(manager + w.Count, out var count))
            countInGame = (int)count;

        var slots = _memory.ReadUInt32(manager + w.SlotArray);
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
        return new NpcInfo(
            b.Address,
            b.UInt32(n.Wid),
            (NpcKind)b.Int32(n.Type),
            b.Int32(n.State),
            b.UInt32(n.Target),
            ReadPosition(b, n.Location),
            b.Float(n.Distance),
            ReadName(b.UInt32(n.NamePointer)),
            b.Int32(n.Hp));
    }

    private GroundItem? ReadGroundItem(MemoryBlock b)
    {
        var g = _p.GroundItem;
        return new GroundItem(
            b.Address,
            b.UInt32(g.Id),
            b.UInt32(g.Tid),
            (GroundItemKind)b.Int32(g.Kind),
            ReadPosition(b, g.Location),
            b.Float(g.Distance),
            ReadName(b.UInt32(g.NamePointer)));
    }

    private List<InventoryItem> ReadInventory(uint inventory)
    {
        var result = new List<InventoryItem>();
        var inv = _p.Inventory;
        if (inventory == 0 || !MemoryBlock.TryRead(_memory, inventory, (int)Math.Max(inv.Items, inv.Size) + 4, out var bag))
            return result;

        var size = Math.Min(bag.Int32(inv.Size), MaxInventory);
        if (size <= 0 || !MemoryBlock.TryRead(_memory, bag.UInt32(inv.Items), size * 4, out var cells))
            return result;

        var itemSize = BlockSize(inv.ItemCategory, inv.ItemTid, inv.ItemCount, inv.ItemEssence, inv.FoodEssence);
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

            result.Add(new InventoryItem(slot, item.UInt32(inv.ItemTid), category, item.Int32(inv.ItemCount), potion, loyalty));
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
            if (MemoryBlock.TryRead(_memory, address, BlockSize(pet.HpRatio, pet.Hunger), out var petBlock))
                cages.Add(new PetInCage(cage, petBlock.Float(pet.HpRatio), petBlock.Int32(pet.Hunger)));
        }

        if (cages.Count == 0)
            return null;

        var active = b.Int32(m.ActiveCage);
        return new PetState(active >= 0 && active < m.CageCount ? active + 1 : null, b.UInt32(m.ActivePetWid), cages);
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

    // Размер блока, в который влезают все поля (каждое — 4 байта)
    private static int BlockSize(params uint[] offsets) => (int)offsets.Max() + 4;
}

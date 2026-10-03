using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.World;

/// <summary>Особые случаи чтения снимка на «бумажной» памяти: то, чего нет в дампе.</summary>
public class WorldReaderTests
{
    private const uint ModuleBase = 0x400000;
    private static readonly ProfileData Profile = new ProfileCatalog().Load("pwclassic136").Data;

    private readonly MemoryImage _memory = new();

    // Минимальный мир: база → game → перс с ником; мир с пустыми списками
    private uint BuildWorld(uint petManager = 0)
    {
        const uint basePtr = 0x1000_0000, game = 0x1001_0000, host = 0x1002_0000, world = 0x1003_0000, name = 0x1004_0000;
        _memory.WriteUInt32(ModuleBase + Profile.Base.BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + Profile.Base.Game, game);
        _memory.WriteUInt32(game + Profile.Host.Struct, host);
        _memory.WriteUInt32(game + Profile.World.World, world);
        _memory.Map(host, 0x1000);
        _memory.WriteUInt32(host + Profile.Host.NamePointer, name);
        _memory.WriteBytes(name, System.Text.Encoding.Unicode.GetBytes("Воин\0"));
        _memory.WriteUInt32(host + Profile.Host.Hp, 100);
        _memory.WriteUInt32(host + Profile.Host.PetManager, petManager);
        _memory.Map(world, 0x100);
        return host;
    }

    private WorldReader Reader() => new(_memory, ModuleBase, Profile);

    [Fact]
    public void NoPetIsNormal()
    {
        BuildWorld(petManager: 0);

        var world = Reader().Read();

        Assert.Null(world.Pet);
        Assert.Equal("Воин", world.Host.Name);
        Assert.Empty(world.Npcs);
    }

    [Fact]
    public void EmptyCagesMeanNoPet()
    {
        const uint manager = 0x2000_0000;
        BuildWorld(petManager: manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.PetManager.ActiveCage, unchecked((uint)-1));

        Assert.Null(Reader().Read().Pet);
    }

    [Fact]
    public void DeadPetInCageIsKnown()
    {
        const uint manager = 0x2000_0000, pet = 0x2001_0000;
        BuildWorld(petManager: manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.PetManager.ActiveCage, unchecked((uint)-1));
        _memory.WriteUInt32(manager + Profile.PetManager.Cages + 2 * 4, pet); // клетка 3
        _memory.Map(pet, 0x100);

        var state = Reader().Read().Pet;

        Assert.NotNull(state);
        Assert.False(state!.IsSummoned);
        Assert.False(state.InCage(3)!.IsAlive);
    }

    [Fact]
    public void CharacterSelectScreenIsNotReady()
    {
        // game есть, персонажа нет
        _memory.WriteUInt32(ModuleBase + Profile.Base.BasePointer, 0x1000_0000);
        _memory.WriteUInt32(0x1000_0000 + Profile.Base.Game, 0x1001_0000);
        _memory.Map(0x1001_0000, 0x100);

        var error = Assert.Throws<WorldNotReadyException>(() => Reader().Read());
        Assert.Contains("Персонаж не в мире", error.Message);
    }

    [Fact]
    public void ClientWithoutGameIsNotReady()
    {
        Assert.Throws<WorldNotReadyException>(() => Reader().Read());
    }

    // Ресурс на земле с записью в справочнике: указатель на название ведёт в запись +8
    private void AddResource(uint slots, int slot, uint address, uint tid, uint kind, uint tool, uint quest, uint material, string name)
    {
        var g = Profile.GroundItem;
        var m = Profile.MineEssence;
        uint node = address + 0x800, record = address + 0x1000;
        _memory.Map(node, 0x10);
        _memory.WriteUInt32(slots + (uint)slot * 4, node);
        _memory.WriteUInt32(node + Profile.World.ObjectInSlot, address);
        _memory.Map(address, 0x400);
        _memory.WriteUInt32(address + g.Tid, tid);
        _memory.WriteUInt32(address + g.Kind, kind);
        _memory.WriteUInt32(address + g.NamePointer, record + m.NameInRecord);
        _memory.Map(record, (int)m.Size);
        _memory.WriteUInt32(record + m.Id, tid);
        _memory.WriteBytes(record + m.NameInRecord, System.Text.Encoding.Unicode.GetBytes(name + "\0"));
        _memory.WriteUInt32(record + m.Tool, tool);
        _memory.WriteUInt32(record + m.Quest, quest);
        if (material != 0)
        {
            _memory.WriteUInt32(record + m.Materials + 8, material); // не обязательно в первой ячейке
            _memory.WriteBytes(record + m.Materials + 12, System.BitConverter.GetBytes(1f));
        }
        _memory.WriteUInt32(record + m.Amounts, 2);
        _memory.WriteBytes(record + m.Amounts + 4, System.BitConverter.GetBytes(0.9f));
        _memory.WriteUInt32(record + m.Amounts + 8, 4);
        _memory.WriteBytes(record + m.Amounts + 12, System.BitConverter.GetBytes(0.1f));
    }

    [Fact]
    public void ResourcesReadWithTheirYieldAndNonResourcesMarked()
    {
        BuildWorld();
        const uint manager = 0x3000_0000, slots = 0x3001_0000;
        _memory.WriteUInt32(0x1003_0000 + Profile.World.GroundItems.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + Profile.World.GroundItems.SlotArray, slots);
        _memory.WriteUInt32(manager + Profile.World.GroundItems.Count, 4);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        AddResource(slots, 1, 0x4000_0000, 3074, 2, tool: 3073, quest: 0, material: 795, "Высохший древесный корень");
        AddResource(slots, 2, 0x4100_0000, 3405, 2, tool: 0, quest: 1093, material: 0, "Безымянный труп");
        AddResource(slots, 3, 0x4200_0000, 9999, 2, tool: 23652, quest: 0, material: 777, "Особый ресурс");
        AddResource(slots, 4, 0x4300_0000, 3044, 3, tool: 0, quest: 0, material: 0, "Монета");

        var world = Reader().Read();

        // Обычный ресурс — кирка без квеста; квестовый и под особый инструмент — «нересурсы»; монета — не ресурс вовсе
        Assert.Equal(
            [("Высохший древесный корень", false), ("Безымянный труп", true), ("Особый ресурс", true), ("Монета", false)],
            world.GroundItems.Select(i => (i.Name, i.Special)));
        var root = world.GroundItems[0].Mine!;
        Assert.Equal((3073u, 0u), (root.Tool, root.Quest));
        Assert.Equal(4, Assert.Single(root.Yields, y => y.Key == 795).Value);
        Assert.Equal(1093u, world.GroundItems[1].Mine!.Quest);
        Assert.Null(world.GroundItems[3].Mine);
        Assert.Equal(4, world.GroundItemCountInGame);
    }

    [Fact]
    public void ChainedSlotsAreAllRead()
    {
        // Две записи в одной ячейке хэш-таблицы: старый бот видел только первую
        BuildWorld();
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node1 = 0x3002_0000, node2 = 0x3002_0100, mob1 = 0x3003_0000, mob2 = 0x3004_0000;
        _memory.WriteUInt32(0x1003_0000 + Profile.World.Npcs.Manager, manager);
        _memory.WriteUInt32(manager + Profile.World.Npcs.SlotArray, slots);
        _memory.WriteUInt32(manager + Profile.World.Npcs.Count, 2);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        _memory.WriteUInt32(slots + 5 * 4, node1);
        _memory.WriteUInt32(node1, node2);
        _memory.WriteUInt32(node1 + Profile.World.ObjectInSlot, mob1);
        _memory.WriteUInt32(node2, 0);
        _memory.WriteUInt32(node2 + Profile.World.ObjectInSlot, mob2);
        foreach (var (mob, wid) in new[] { (mob1, 0x80000001u), (mob2, 0x80000002u) })
        {
            _memory.Map(mob, 0x400);
            _memory.WriteUInt32(mob + Profile.Npc.Wid, wid);
            _memory.WriteUInt32(mob + Profile.Npc.Type, 6);
        }

        var world = Reader().Read();

        Assert.Equal([0x80000001u, 0x80000002u], world.Npcs.Select(n => n.Wid));
        Assert.Equal(2, world.NpcCountInGame);
    }

    [Fact]
    public void VanishedMobIsSkipped()
    {
        // Узел есть, а объект уже освобождён (моб исчез посреди чтения)
        BuildWorld();
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node = 0x3002_0000;
        _memory.WriteUInt32(0x1003_0000 + Profile.World.Npcs.Manager, manager);
        _memory.WriteUInt32(manager + Profile.World.Npcs.SlotArray, slots);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        _memory.WriteUInt32(slots, node);
        _memory.WriteUInt32(node + Profile.World.ObjectInSlot, 0x7000_0000);

        Assert.Empty(Reader().Read().Npcs);
    }

    [Theory]
    [InlineData(NpcInfo.StateCasting, 0xA0126CE5u)]   // кастует — цель каста
    [InlineData(NpcInfo.StateAttacking, 0x0130ECE0u)] // бьёт рукой — цель удара
    [InlineData(1, 0xFFFFFFFFu)]                      // стоит — запасные поля не смотрим
    public void MobWithEmptyTargetFieldTargetsWhomItHits(int state, uint expected)
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        var mob = ComebackWorldWithMob(p);
        _memory.WriteUInt32(mob + p.Npc.State, (uint)state);
        _memory.WriteUInt32(mob + p.Npc.Target, 0xFFFFFFFF);
        _memory.WriteUInt32(mob + p.Npc.CastTarget, 0xA0126CE5);
        _memory.WriteUInt32(mob + p.Npc.AttackTarget, 0x0130ECE0);

        var npc = Assert.Single(new WorldReader(_memory, ModuleBase, p).Read().Npcs);

        Assert.Equal(expected, npc.TargetWid);
    }

    [Fact]
    public void ComebackReadsGatherProgress()
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        ComebackWorldWithMob(p);
        const uint host = 0x1002_0000;
        _memory.WriteBytes(host + p.Host.GatherIdle, [0]);
        _memory.WriteUInt32(host + p.Host.GatherElapsed, 1800);
        _memory.WriteUInt32(host + p.Host.GatherTotal, 8000);

        Assert.Equal(new GatherProgress(true, 1800, 8000), new WorldReader(_memory, ModuleBase, p).Read().Host.Gather);
    }

    [Fact]
    public void ComebackReadsCastingSkill()
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        ComebackWorldWithMob(p);
        const uint host = 0x1002_0000, skill = 0x5000_0000;
        _memory.Map(skill, 0x40);
        _memory.WriteUInt32(skill + p.Skill.Id, 330);
        _memory.WriteUInt32(host + p.Host.CastingSkill, skill);

        Assert.Equal(330, new WorldReader(_memory, ModuleBase, p).Read().Host.CastingSkillId);
        _memory.WriteUInt32(host + p.Host.CastingSkill, 0);
        Assert.Equal(0, new WorldReader(_memory, ModuleBase, p).Read().Host.CastingSkillId);
    }

    // Мир Comeback 1.4.6 с одним мобом; возвращает адрес моба
    private uint ComebackWorldWithMob(ProfileData p)
    {
        const uint basePtr = 0x1000_0000, game = 0x1001_0000, host = 0x1002_0000, world = 0x1003_0000, name = 0x1004_0000;
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node = 0x3002_0000, mob = 0x3003_0000;
        _memory.WriteUInt32(ModuleBase + p.Base.BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + p.Base.Game, game);
        _memory.WriteUInt32(game + p.Host.Struct, host);
        _memory.WriteUInt32(game + p.World.World, world);
        _memory.Map(host, 0x2000);
        _memory.WriteUInt32(host + p.Host.NamePointer, name);
        _memory.WriteBytes(name, System.Text.Encoding.Unicode.GetBytes("Кот\0"));
        _memory.Map(world, 0x100);
        _memory.WriteUInt32(world + p.World.Npcs.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + p.World.Npcs.SlotArray, slots);
        _memory.WriteUInt32(manager + p.World.Npcs.Count, 1);
        _memory.Map(slots, p.World.SlotCount * 4);
        _memory.WriteUInt32(slots + 7 * 4, node);
        _memory.WriteUInt32(node + p.World.ObjectInSlot, mob);
        _memory.Map(mob, 0x400);
        _memory.WriteUInt32(mob + p.Npc.Wid, 0x80100018);
        _memory.WriteUInt32(mob + p.Npc.Type, 6);
        return mob;
    }

    [Fact]
    public void ComebackReadsMobsFromOwnLayoutAndSkipsUnknownFields()
    {
        // У Comeback 1.4.6 у менеджера мобов своя раскладка (массив +0x20, у предметов +0x1C); сумки и скиллов ещё нет (0), пета нет (менеджер 0)
        var p = new ProfileCatalog().Load("comeback146").Data;
        const uint basePtr = 0x1000_0000, game = 0x1001_0000, host = 0x1002_0000, world = 0x1003_0000, name = 0x1004_0000;
        const uint manager = 0x3000_0000, slots = 0x3001_0000, node = 0x3002_0000, mob = 0x3003_0000;
        _memory.WriteUInt32(ModuleBase + p.Base.BasePointer, basePtr);
        _memory.WriteUInt32(basePtr + p.Base.Game, game);
        _memory.WriteUInt32(game + p.Host.Struct, host);
        _memory.WriteUInt32(game + p.World.World, world);
        _memory.Map(host, 0x2000); // менеджер петов у 1.4.6 по +0x1980
        _memory.WriteUInt32(host, 0x00DE_AD00); // vtable: не должна прочитаться как цель, сумка или скиллы
        _memory.WriteUInt32(host + p.Host.NamePointer, name);
        _memory.WriteBytes(name, System.Text.Encoding.Unicode.GetBytes("Кот\0"));
        _memory.WriteUInt32(host + p.Host.Hp, 84);
        _memory.Map(world, 0x100);
        _memory.WriteUInt32(world + p.World.Npcs.Manager, manager);
        _memory.Map(manager, 0x100);
        _memory.WriteUInt32(manager + p.World.Npcs.SlotArray, slots);
        _memory.WriteUInt32(manager + p.World.Npcs.Count, 1);
        _memory.Map(slots, p.World.SlotCount * 4);
        _memory.WriteUInt32(slots + 7 * 4, node);
        _memory.WriteUInt32(node + p.World.ObjectInSlot, mob);
        _memory.Map(mob, 0x400);
        _memory.WriteUInt32(mob, 0x00BE_EF00);
        _memory.WriteUInt32(mob + p.Npc.Wid, 0x80100018);
        _memory.WriteUInt32(mob + p.Npc.Type, 6);

        var state = new WorldReader(_memory, ModuleBase, p).Read();

        Assert.Equal("Кот", state.Host.Name);
        Assert.Equal(84, state.Host.Hp);
        Assert.Equal(0u, state.Host.TargetWid);
        Assert.Empty(state.Inventory);
        Assert.Empty(state.Skills);
        Assert.Null(state.Pet);
        var npc = Assert.Single(state.Npcs);
        Assert.Equal(0x80100018u, npc.Wid);
        Assert.Equal(0, npc.State);
        Assert.Equal(0u, npc.TargetWid);
        Assert.Equal(1, state.NpcCountInGame);
    }
}

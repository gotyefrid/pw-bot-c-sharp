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
    [InlineData(NpcInfo.StateCasting, 0xA0126CE5u)] // кастует — цель каста
    [InlineData(2, 0xFFFFFFFFu)]                     // бьёт рукой — обычное поле, цель каста не смотрим
    public void CastingMobTargetsWhomItCastsAt(int state, uint expected)
    {
        var p = new ProfileCatalog().Load("comeback146").Data;
        var mob = ComebackWorldWithMob(p);
        _memory.WriteUInt32(mob + p.Npc.State, (uint)state);
        _memory.WriteUInt32(mob + p.Npc.Target, 0xFFFFFFFF);
        _memory.WriteUInt32(mob + p.Npc.CastTarget, 0xA0126CE5);

        var npc = Assert.Single(new WorldReader(_memory, ModuleBase, p).Read().Npcs);

        Assert.Equal(expected, npc.TargetWid);
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

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
        _memory.WriteUInt32(0x1003_0000 + Profile.World.Npcs, manager);
        _memory.WriteUInt32(manager + Profile.World.SlotArray, slots);
        _memory.WriteUInt32(manager + Profile.World.Count, 2);
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
        _memory.WriteUInt32(0x1003_0000 + Profile.World.Npcs, manager);
        _memory.WriteUInt32(manager + Profile.World.SlotArray, slots);
        _memory.Map(slots, Profile.World.SlotCount * 4);
        _memory.WriteUInt32(slots, node);
        _memory.WriteUInt32(node + Profile.World.ObjectInSlot, 0x7000_0000);

        Assert.Empty(Reader().Read().Npcs);
    }
}

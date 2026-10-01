using System.Linq;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.World;

public class NearbyNamesTests
{
    private readonly FakeWorld _world = new();

    [Fact]
    public void MobsGroupedByNameMostCommonFirst()
    {
        _world.AddMob(1, "Горный варвар", 40);
        _world.AddMob(2, "Сидящий волк", 30);
        _world.AddMob(3, "Сидящий волк", 12);
        _world.AddMob(4, "Сидящий волк", 50);
        _world.AddMob(5, "Колючий дикобраз", 20);
        _world.AddMob(6, "Сидящий волк", 5, state: NpcInfo.StateDead); // труп не считаем
        _world.Npcs.Add(new NpcInfo(0, 7, NpcKind.Npc, 1, 0, default, 3, "Отшельник", 0)); // NPC — не моб

        var names = NearbyNames.Mobs(_world.Snapshot());

        Assert.Equal(["Сидящий волк", "Колючий дикобраз", "Горный варвар"], names.Select(n => n.ToString()));
        Assert.Equal(3, names[0].Count);
        Assert.Equal(12f, names[0].Nearest);
    }

    [Fact]
    public void GroundItemsWithoutResources()
    {
        _world.Ground.Add(new GroundItem(0, 1, 3044, GroundItemKind.Money, default, 5, "Монета"));
        _world.Ground.Add(new GroundItem(0, 2, 3044, GroundItemKind.Money, default, 8, "Монета"));
        _world.Ground.Add(new GroundItem(0, 3, 8094, GroundItemKind.Item, default, 3, "Мягкий мех"));
        _world.Ground.Add(new GroundItem(0, 4, 3089, GroundItemKind.Resource, default, 2, "Шахта крупного угля"));

        var names = NearbyNames.GroundItems(_world.Snapshot());

        Assert.Equal(["Монета", "Мягкий мех"], names.Select(n => n.ToString()));
    }

    [Fact]
    public void MobLevelsShownInsteadOfCount()
    {
        _world.Npcs.Add(new NpcInfo(0, 1, NpcKind.Mob, 1, 0, default, 10, "Сидящий волк", 0) { Level = 10 });
        _world.Npcs.Add(new NpcInfo(0, 2, NpcKind.Mob, 1, 0, default, 20, "Сидящий волк", 0) { Level = 10 });
        _world.Npcs.Add(new NpcInfo(0, 3, NpcKind.Mob, 1, 0, default, 30, "Волк-вожак", 0) { Level = 12 });
        _world.Npcs.Add(new NpcInfo(0, 4, NpcKind.Mob, 1, 0, default, 30, "Волк-вожак", 0) { Level = 14 });

        var names = NearbyNames.Mobs(_world.Snapshot());

        Assert.Equal(["Сидящий волк (10)", "Волк-вожак (12–14)"], names.Select(n => n.ToString()));
    }

    [Fact]
    public void NothingAround()
    {
        Assert.Empty(NearbyNames.Mobs(_world.Snapshot()));
        Assert.Empty(NearbyNames.GroundItems(_world.Snapshot()));
    }
}

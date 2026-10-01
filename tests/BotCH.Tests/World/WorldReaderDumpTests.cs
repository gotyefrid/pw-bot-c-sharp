using System;
using System.IO;
using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.World;

/// <summary>
/// Чтение снимка на дампе живого клиента (Fixtures/world-pwclassic136.dump, снят «Probe dump» в бою с волком).
/// Что было в игре в этот момент — в world-pwclassic136.txt рядом. Защищает читатели от поломок при рефакторинге.
/// </summary>
public class WorldReaderDumpTests
{
    private static readonly Lazy<WorldState> World = new(() =>
    {
        var dump = MemoryDump.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world-pwclassic136.dump"));
        var profile = new ProfileCatalog().Load(dump.ServerId).Data;
        string? Name(int id) => id == 299 ? "Жалящий рой" : null;
        return new WorldReader(dump.Image, dump.ModuleBase, profile, Name).Read();
    });

    private static WorldState W => World.Value;

    [Fact]
    public void Host()
    {
        var h = W.Host;

        Assert.Equal("Купчихан", h.Name);
        Assert.Equal(10, h.Level);
        Assert.Equal(0x0015A420u, h.Wid);
        Assert.Equal((458, 479), (h.Hp, h.MaxHp));
        Assert.Equal((490, 490), (h.Mp, h.MaxMp));
        Assert.InRange(h.PetFoodCooldownMs, 1, 60_000); // в момент дампа шла перезарядка корма
        Assert.Equal(-1797.8f, h.Position.X, 0.1f);
    }

    [Fact]
    public void TargetIsWolfAttackingPet()
    {
        var target = W.Target;

        Assert.NotNull(target);
        Assert.Equal("Сидящий волк", target!.Name);
        Assert.InRange(target.Level, 1, 150);
        Assert.Equal(395, target.Hp);
        Assert.Equal(NpcKind.Mob, target.Kind);
        Assert.Equal(W.Pet!.ActiveWid, target.TargetWid);
    }

    [Fact]
    public void AllNpcsAreReadWithNames()
    {
        Assert.Equal(9, W.Npcs.Count);
        Assert.Equal(W.NpcCountInGame, W.Npcs.Count);
        Assert.All(W.Npcs, n => Assert.NotEmpty(n.Name));
        Assert.Equal(3, W.Mobs.Count(n => n.Name == "Сидящий волк"));
        Assert.Single(W.Npcs, n => n.Kind == NpcKind.Npc);
    }

    [Fact]
    public void PetIsSummonedAndInMobList()
    {
        var pet = W.Pet;

        Assert.NotNull(pet);
        Assert.True(pet!.IsSummoned);
        Assert.Equal(1, pet.ActiveCage);
        Assert.Equal(78, pet.InCage(1)!.HpPercent);
        Assert.False(pet.InCage(1)!.IsHungry);
        Assert.Contains(W.Npcs, n => n.Wid == pet.ActiveWid && n.Kind == NpcKind.Pet && n.Name == "Молодой свирепый волк");
    }

    [Fact]
    public void GroundItemsWithKindsAndNames()
    {
        Assert.Equal(26, W.GroundItems.Count);
        Assert.Contains(W.GroundItems, i => i.Kind == GroundItemKind.Resource && i.Name == "Шахта крупного угля");
        Assert.All(W.GroundItems.Where(i => i.Tid == 3044), i => Assert.Equal((GroundItemKind.Money, "Монета"), (i.Kind, i.Name)));
    }

    [Fact]
    public void PotionsAndPetFood()
    {
        var potions = W.Inventory.Where(i => i.Potion is not null).ToList();

        Assert.Equal(26, W.Inventory.Count);
        Assert.Equal([1, 2, 3, 4], potions.Select(p => p.Slot));
        var hp80 = Assert.Single(potions, p => p.Tid == 8618);
        Assert.Equal((25, 5, 80, 10), (hp80.Count, hp80.Potion!.RequiredLevel, hp80.Potion.Hp, hp80.Potion.HpSeconds));
        var food = Assert.Single(W.Inventory, i => i.IsPetFood);
        Assert.Equal((0, 12842u, 153, 10), (food.Slot, food.Tid, food.Count, food.FoodLoyalty!.Value));
    }

    [Fact]
    public void SkillsWithNamesFromCallback()
    {
        Assert.Equal([167, 299, 329, 330], W.Skills.Select(s => s.Id));
        Assert.Equal("Жалящий рой", W.Skill(299)!.Name);
        Assert.Null(W.Skill(167)!.Name);
        Assert.All(W.Skills, s => Assert.True(s.IsReady));
    }
}

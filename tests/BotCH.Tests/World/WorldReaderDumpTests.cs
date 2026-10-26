using System;
using System.IO;
using System.Linq;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.World;
using Xunit;

namespace BotCH.Tests.World;

/// <summary>
/// Чтение снимка на дампе живого клиента (Fixtures/world-pwclassic136.dump, снят «Probe dump» с client.exe в Портовом городе,
/// ник заменён на «Персонаж»). Что было в игре в этот момент — в world-pwclassic136.txt рядом. Защищает читатели от поломок
/// при рефакторинге.
/// </summary>
public class WorldReaderDumpTests
{
    private static readonly Lazy<WorldState> World = new(() =>
    {
        var dump = MemoryDump.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "world-pwclassic136.dump"));
        var profile = new ProfileCatalog().Load(dump.ServerId).Data;
        string? Name(int id) => id == 299 ? "Жалящий рой" : null;
        // Указатель на базу у этого клиента (exe без ASLR, база модуля 0x400000) — [0xE45740]: так его нашёл BasePointerSearch
        return new WorldReader(dump.Image, new GameRoots(dump.Image, 0xE45740, profile), profile, Name).Read();
    });

    private static WorldState W => World.Value;

    [Fact]
    public void Host()
    {
        var h = W.Host;

        Assert.Equal("Персонаж", h.Name);
        Assert.Equal(22, h.Level);
        Assert.Equal(0x0015A420u, h.Wid);
        Assert.Equal((1013, 1013), (h.Hp, h.MaxHp));
        Assert.Equal((1068, 1068), (h.Mp, h.MaxMp));
        Assert.Equal(267.5f, h.Position.X, 0.1f);
        Assert.Equal(0u, h.TargetWid);
        Assert.False(h.IsCasting);
    }

    [Fact]
    public void AllNpcsAreReadWithNames()
    {
        Assert.Equal(31, W.Npcs.Count);
        Assert.Equal(W.NpcCountInGame, W.Npcs.Count);
        Assert.All(W.Npcs, n => Assert.NotEmpty(n.Name));
        Assert.Equal(13, W.Mobs.Count(n => n.Name == "Призрачный бумажный фонарик"));
        var teleport = Assert.Single(W.Npcs, n => n.Name == "Мастер телепорта Цюй Ся'эр");
        Assert.Equal((NpcKind.Npc, 60), (teleport.Kind, teleport.Level));
    }

    [Fact]
    public void PetsInCagesWithNamesAndHabitats()
    {
        var pet = W.Pet;

        Assert.NotNull(pet);
        Assert.False(pet!.IsSummoned);
        Assert.Equal(("Молодой узорчатый скорпион", PetHabitat.Ground, 73), (pet.InCage(1)!.Name, pet.InCage(1)!.Habitat, pet.InCage(1)!.HpPercent));
        Assert.Equal(("Молодая лиственная пчела", PetHabitat.Air, 10), (pet.InCage(2)!.Name, pet.InCage(2)!.Habitat, pet.InCage(2)!.HpPercent));
    }

    [Fact]
    public void GroundResourceWithName()
    {
        var ore = Assert.Single(W.GroundItems);

        Assert.Equal(W.GroundItemCountInGame, W.GroundItems.Count);
        Assert.Equal((GroundItemKind.Resource, "Черное железо", 3080u), (ore.Kind, ore.Name, ore.Tid));
    }

    [Fact]
    public void PotionsAndPetFood()
    {
        var potions = W.Inventory.Where(i => i.Potion is not null).ToList();

        Assert.Equal(22, W.Inventory.Count);
        Assert.Equal(32, W.InventorySlots);
        Assert.False(W.BagFull);
        Assert.Equal([0, 2, 13, 17], potions.Select(p => p.Slot));
        var hp410 = Assert.Single(potions, p => p.Tid == 8621);
        Assert.Equal((5, 30, 410, 10), (hp410.Count, hp410.Potion!.RequiredLevel, hp410.Potion.Hp, hp410.Potion.HpSeconds));
        var mp = Assert.Single(potions, p => p.Tid == 8650);
        Assert.Equal((0, 330), (mp.Potion!.Hp, mp.Potion.Mp));
        Assert.Equal([(1, 8796u, 76, 10), (11, 9757u, 3, 20)],
            W.Inventory.Where(i => i.IsPetFood).Select(f => (f.Slot, f.Tid, f.Count, f.FoodLoyalty!.Value)));
    }

    [Fact]
    public void SkillsWithNamesFromCallback()
    {
        Assert.Equal([299, 328, 330, 329, 167], W.Skills.Select(s => s.Id));
        Assert.Equal("Жалящий рой", W.Skill(299)!.Name);
        Assert.Null(W.Skill(167)!.Name);
        Assert.All(W.Skills, s => Assert.True(s.IsReady));
    }
}

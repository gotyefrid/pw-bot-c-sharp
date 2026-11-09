using Notes.Core.Brain;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

public class SkillRangeBookTests
{
    [Fact]
    public void LearnsRangeFromDistanceAtCastStart()
    {
        // Клиент подбежал и начал каст в 17,5 м — это дальность; дальше каст не начинался
        var world = new FakeWorld();
        var book = new SkillRangeBook();
        world.TargetWid = world.AddMob(0x80000001, "Волк", 17.5f).Wid;
        book.Observe(world.Snapshot(), healPet: 330);
        Assert.Null(book.Of(299));

        world.Casting = true;
        world.CastingSkillId = 299;
        Assert.NotNull(book.Observe(world.Snapshot(), healPet: 330));

        Assert.Equal(17.5f, book.Of(299)!.Value, 1);
    }

    [Fact]
    public void KeepsLargestAndCountsOnlyCastStart()
    {
        var world = new FakeWorld();
        var book = new SkillRangeBook();
        var mob = world.AddMob(0x80000001, "Волк", 17f);
        world.TargetWid = mob.Wid;
        world.Casting = true;
        world.CastingSkillId = 299;
        book.Observe(world.Snapshot(), 330);

        // Тот же каст идёт дальше — моб отошёл на 25 м: не начало каста, не считаем
        world.Npcs.Clear();
        world.AddMob(0x80000001, "Волк", 25f);
        book.Observe(world.Snapshot(), 330);
        Assert.Equal(17f, book.Of(299)!.Value, 1);

        // Новый каст ближе — дальность не уменьшается
        world.Casting = false;
        book.Observe(world.Snapshot(), 330);
        world.Npcs.Clear();
        world.AddMob(0x80000001, "Волк", 10f);
        world.Casting = true;
        book.Observe(world.Snapshot(), 330);
        Assert.Equal(17f, book.Of(299)!.Value, 1);
    }

    [Fact]
    public void PetHealIsMeasuredToPet()
    {
        // Лечение пета — расстояние до пета, а не до цели перса
        var world = new FakeWorld();
        var book = new SkillRangeBook();
        world.TargetWid = world.AddMob(0x80000001, "Волк", 5f).Wid;
        world.SetPet(1, 0.5f);
        world.Npcs.RemoveAll(n => n.Kind == NpcKind.Pet);
        world.Npcs.Add(new NpcInfo(1, FakeWorld.PetWid, NpcKind.Pet, NpcState.Standing, 0, new Position(12, 0, 0), "Пет", 0));
        world.Casting = true;
        world.CastingSkillId = 330;

        book.Observe(world.Snapshot(), healPet: 330);

        Assert.Equal(12f, book.Of(330)!.Value, 1);
    }
}

using Notes.Core.Brain;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>Самый опасный моб: порядок «бьёт меня» → «бьёт меня или пета» → ближе.</summary>
public class TargetSelectorTests
{
    private readonly FakeWorld _world = new();

    public TargetSelectorTests() => _world.SetPet(1);

    [Fact]
    public void HitsMeBeatsNearerMobHittingPet()
    {
        _world.AddMob(0x80000001, "Волк", 2, targetWid: FakeWorld.PetWid);
        _world.AddMob(0x80000002, "Кабан", 9, targetWid: FakeWorld.HostWid);

        var mob = TargetSelector.MostDangerous(_world.Snapshot(), hitsUsFirst: true, petTakesAggro: true, out var threat);

        Assert.Equal((0x80000002u, Threat.HitsMe), (mob!.Wid, threat));
    }

    [Fact]
    public void WithoutPetRuleNearestOfThoseHittingUs()
    {
        _world.AddMob(0x80000001, "Волк", 2, targetWid: FakeWorld.PetWid);
        _world.AddMob(0x80000002, "Кабан", 9, targetWid: FakeWorld.HostWid);

        var mob = TargetSelector.MostDangerous(_world.Snapshot(), hitsUsFirst: true, petTakesAggro: false, out var threat);

        Assert.Equal((0x80000001u, Threat.HitsUs), (mob!.Wid, threat));
    }

    [Fact]
    public void RulesOffNobodyIsDangerous()
    {
        _world.AddMob(0x80000001, "Волк", 2, targetWid: FakeWorld.HostWid);

        Assert.Null(TargetSelector.MostDangerous(_world.Snapshot(), hitsUsFirst: false, petTakesAggro: false, out _));
    }

    // Было: летающая змея в 10 м по карте, но на 29 м выше, считалась ближе соболя в 15 м на земле
    [Fact]
    public void NearestCountsHeight()
    {
        var snake = _world.AddMob(0x80000001, "Змея", 10);
        _world.Replace(snake, m => m with { Position = m.Position with { Height = 29 } });
        _world.AddMob(0x80000002, "Соболь", 15);

        var mob = TargetSelector.Nearest(_world.Snapshot(), new TargetSettings(), []);

        Assert.Equal(0x80000002u, mob!.Wid);
    }

    [Fact]
    public void AggressorNearestCountsHeight()
    {
        var snake = _world.AddMob(0x80000001, "Змея", 10, targetWid: FakeWorld.HostWid);
        _world.Replace(snake, m => m with { Position = m.Position with { Height = 29 } });
        _world.AddMob(0x80000002, "Соболь", 15, targetWid: FakeWorld.HostWid);

        Assert.Equal(0x80000002u, TargetSelector.Aggressor(_world.Snapshot())!.Wid);
    }
}

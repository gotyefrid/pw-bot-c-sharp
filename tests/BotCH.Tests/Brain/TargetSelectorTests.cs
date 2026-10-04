using BotCH.Core.Brain;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Brain;

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
}

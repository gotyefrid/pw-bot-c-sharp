using System.Linq;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.World;

public class AttackTrackerTests
{
    private const uint Bee = 0x80104B25;
    private readonly FakeWorld _world = new();
    private readonly AttackTracker _tracker = new();

    private static AttackEvent Hit(uint node, int lived, int damage, uint target = FakeWorld.PetWid, int skill = 0)
        => new(node, lived, Bee, skill, [new AttackHit(target, damage, 0)]);

    private int Fresh(params AttackEvent[] attacks) => _tracker.Update(_world.Snapshot() with { Attacks = attacks }).Count;

    [Fact]
    public void SameAttackInSeveralSnapshotsCountsOnce()
    {
        // Удар держится в списке игры 0,4…2 с — в снимках раз в 250 мс он виден несколько раз
        Assert.Equal(1, Fresh(Hit(0x100, 30, 85)));
        Assert.Equal(0, Fresh(Hit(0x100, 280, 85)));
        Assert.Equal(0, Fresh(Hit(0x100, 530, 85)));
    }

    [Fact]
    public void ReusedNodeIsNewAttack()
    {
        // Узел после удара игра отдаёт следующему: время в списке начинается заново, даже если урон тот же
        Fresh(Hit(0x100, 400, 498));

        Assert.Equal(1, Fresh(Hit(0x100, 20, 498)));
        Assert.Equal(1, Fresh(Hit(0x100, 300, 79)));
        Assert.Equal(1, Fresh(Hit(0x100, 400, 79, FakeWorld.HostWid)));
        Assert.Equal(1, Fresh(Hit(0x100, 500, 79, FakeWorld.HostWid, skill: 24)));
    }

    [Fact]
    public void AttackGoneAndBackIsNew()
    {
        Fresh(Hit(0x100, 30, 85), Hit(0x200, 30, 498));
        Fresh(Hit(0x200, 280, 498));

        var fresh = _tracker.Update(_world.Snapshot() with { Attacks = [Hit(0x100, 10, 85), Hit(0x200, 530, 498)] });

        Assert.Equal(0x100u, Assert.Single(fresh).Node);
    }
}

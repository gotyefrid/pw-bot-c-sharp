using System.Collections.Generic;
using System.Linq;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.World;

public class DamageLogTests
{
    private const uint Bee = 0x80104B25;
    private const uint Widow = 0x80104B21;
    private readonly FakeWorld _world = new();
    private readonly List<LogEntry> _log = [];
    private readonly DamageLog _damage;
    private uint _node = 0x100;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public DamageLogTests()
    {
        _damage = new DamageLog(new Logger().AddSink(new ListSink(_log)).For(LogSources.Damage));
        _world.AddMob(Bee, "Боевая пчела-убийца", 3);
        _world.AddMob(Widow, "Паучья вдова", 6);
        // Скорпион ур. 37: 1067 HP в окне пета, сейчас 90 % — 960
        _world.SetPets(1, new PetInCage(1, 0.9f, 0) { Level = 37, MaxHp = 1067 });
    }

    private AttackEvent Hit(uint attacker, uint target, int damage, string? skill = null)
        => new(_node++, 30, attacker, skill is null ? 0 : 1, [new AttackHit(target, damage, 0)]) { SkillName = skill };

    private List<string> Lines(params AttackEvent[] attacks)
    {
        _damage.Observe(_world.Snapshot() with { Attacks = attacks });
        return _log.Select(e => e.Message).ToList();
    }

    [Fact]
    public void OurHitsWithoutTargetHp()
    {
        Assert.Equal(["Пет → Боевая пчела-убийца: 519 (Яростная атака)", "Вы → Паучья вдова: 228"],
            Lines(Hit(FakeWorld.PetWid, Bee, 519, "Яростная атака"), Hit(FakeWorld.HostWid, Widow, 228)));
        Assert.All(_log, e => Assert.Equal(LogSources.Damage, e.Source));
    }

    [Fact]
    public void HitsOnUsWithHpAfterTheHit()
    {
        // Игра снижает HP позже, чем удар виден: HP после — HP до удара минус урон
        _world.Hp = 400;
        _world.MaxHp = 500;

        Assert.Equal(["Боевая пчела-убийца → пет: 85, HP 875/1067", "Паучья вдова → вы: 238 (Змеиный яд), HP 162/500"],
            Lines(Hit(Bee, FakeWorld.PetWid, 85), Hit(Widow, FakeWorld.HostWid, 238, "Змеиный яд")));
    }

    [Fact]
    public void LastHitLeavesZero()
    {
        _world.Hp = 100;

        Assert.Equal(["Боевая пчела-убийца → вы: 238, HP 0/500"], Lines(Hit(Bee, FakeWorld.HostWid, 238)));
    }

    [Fact]
    public void PetHpUnknownIsNotShown()
    {
        _world.SetPets(1, new PetInCage(1, 0.9f, 0));

        Assert.Equal(["Боевая пчела-убийца → пет: 85"], Lines(Hit(Bee, FakeWorld.PetWid, 85)));
    }

    [Fact]
    public void HitSeenInSeveralSnapshotsIsWrittenOnce()
    {
        var hit = Hit(Bee, FakeWorld.PetWid, 85);
        Lines(hit);

        Assert.Single(Lines(hit with { LivedMs = 280 }));
    }

    [Fact]
    public void NoDamageAndOtherFightsAreNotWritten()
    {
        // Лечение пета (−2), промах (0), бафф моба на себя, моб бьёт другого моба, игрок бьёт моба
        Assert.Empty(Lines(Hit(FakeWorld.HostWid, FakeWorld.PetWid, -2, "Исцеление питомца"), Hit(FakeWorld.PetWid, Bee, 0),
            Hit(Bee, Bee, -2, "Стойкость"), Hit(Widow, Bee, 300), Hit(0x0012_3456, Widow, 500)));
    }

    [Fact]
    public void UnknownAttackersAreNamedPlainly()
    {
        Assert.Equal(["игрок → вы: 300, HP 100/500", "кто-то → пет: 50, HP 910/1067"],
            Lines(Hit(0x0012_3456, FakeWorld.HostWid, 300), Hit(0x8000_9999, FakeWorld.PetWid, 50)));
    }
}

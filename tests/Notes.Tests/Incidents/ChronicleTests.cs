using System.Collections.Generic;
using System.Linq;
using Notes.Core.Incidents;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Incidents;

/// <summary>Хроника случая: только то, что изменилось между снимками.</summary>
public class ChronicleTests
{
    private const uint Bat = 0x8010_0001;
    private const uint Itori = 0x0012_0001;
    private readonly FakeWorld _world = new();
    private readonly Chronicle _chronicle = new();
    private readonly List<ChatMessage> _chat = [new(100, 1, "привет", 1)];

    public ChronicleTests() => Changes();

    private IReadOnlyList<string> Changes() => _chronicle.Changes(_world.Wait(0.25).Snapshot() with { Chat = _chat.ToList() });

    [Fact]
    public void FirstSnapshotIsStartingPointOnly()
    {
        _world.AddMob(Bat, "Летяга", 10);
        Assert.Empty(new Chronicle().Changes(_world.Snapshot()));
    }

    [Fact]
    public void QuietWorldGivesNothing()
    {
        _world.AddMob(Bat, "Летяга", 10);
        Changes();

        Assert.Empty(Changes());
    }

    [Fact]
    public void AssassinComesOutOfInvisibilityAndKills()
    {
        _world.Players.Add(new PlayerInfo(2, Itori, "Itori", new Position(4, 0, 0)) { Class = PlayerClasses.Assassin, TargetWid = FakeWorld.HostWid });
        Assert.Equal(["появился игрок Itori, убийца, цель → вас, 4,0 м"], Changes());

        _world.Attacks.Add(new AttackEvent(1, 0, Itori, 0, [new AttackHit(FakeWorld.HostWid, 4000, 0)]));
        _world.Hp = 0;
        _chat.Add(new ChatMessage(101, 8, "Itori. Запомните это имя. Так зовут вашего убийцу.", uint.MaxValue));

        Assert.Equal(
            ["HP перса 400 → 0/500", "удар: игрок Itori → вы: 4000", "чат [8] Itori. Запомните это имя. Так зовут вашего убийцу."],
            Changes());
        // Тот же удар в следующем снимке — не новый
        Assert.Empty(Changes());
    }

    [Fact]
    public void MobTakesUsAsTargetAndLeaves()
    {
        var bat = _world.AddMob(Bat, "Летяга", 20);
        Changes();

        _world.Replace(bat, m => m with { TargetWid = FakeWorld.HostWid, State = NpcState.Attacking });
        Assert.Equal(["моб Летяга взял в цель вас, 20,0 м"], Changes());

        _world.Npcs.Clear();
        Assert.Equal(["пропал моб Летяга, 20,0 м"], Changes());
    }

    [Fact]
    public void WalkingIsReportedEveryTenMetres()
    {
        var lines = new List<string>();
        for (var x = 1; x <= 25; x++)
        {
            _world.Position = new Position(x, 0, 0);
            lines.AddRange(Changes());
        }

        Assert.Equal(["перс в (10,0; 0,0; h 0,0)", "перс в (20,0; 0,0; h 0,0)"], lines);
    }

    [Fact]
    public void PetHpAndLoot()
    {
        _world.SetPet(1, hpRatio: 0.5f);
        Changes();

        _world.SetPet(1, hpRatio: 0.2f);
        _world.Ground.Add(new GroundItem(3, 9, 100, GroundItemKind.Item, new Position(0, 0, 3), "Перо"));

        Assert.Equal(["HP пета 50 → 20 %", "на земле появилось Перо, 3,0 м"], Changes());
    }
}

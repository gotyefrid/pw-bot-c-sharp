using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Brain.Follow;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>«Следовать»: идти за главным другой вкладки, бить его цель, брать и сдавать его задания.</summary>
public class FollowModeTests
{
    private const uint LeaderWid = 0x0015B000;
    private const uint Giver = 3518;
    private const int Task = 1204;

    private readonly FakeWorld _me = new() { Name = "Помощник", Quests = [], QuestCandidates = [] };
    private readonly FakeWorld _leader = new() { Name = "Главный", Wid = LeaderWid, Quests = [] };
    private readonly LeaderBoard _board = new();
    private readonly FakeActions _actions = new();
    private readonly BotSettings _settings = new();
    private string? _stopped;
    private IBotRunner? _brain;

    public FollowModeTests()
    {
        _settings.Mode = BotMode.Follow;
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _settings.Follow.Leader = "Главный";
        _me.AddSkill(_settings.Combat.AttackSkillId);
    }

    private IBotRunner Brain
    {
        get
        {
            if (_brain is not null)
                return _brain;
            _brain = BotModes.Create(BotMode.Follow, new ActionRunner(new GameControl(_actions), NullLogger.Instance), new ClassSkills(),
                _settings, NullLogger.Instance, random: new Random(1), leaders: _board);
            _brain.StopRequested += reason => _stopped = reason;
            return _brain;
        }
    }

    // Главный и помощник — в одно время: снимок главного свежий
    private void Tick(double seconds = 0.25)
    {
        _leader.Wait(seconds);
        _board.Publish(_leader.Snapshot());
        Brain.Tick(_me.Wait(seconds).Snapshot());
    }

    private NpcInfo AddNpc(FakeWorld world, uint tid, float distance)
    {
        var npc = new NpcInfo(0x80103F91, 0x80103F91, NpcKind.Npc, NpcState.Standing, 0, new Position(distance, 0, 0), "Старейшина", 0) { Tid = (int)tid };
        world.Npcs.Add(npc);
        return npc;
    }

    private static QuestInfo Quest(int done = 0) => new(Task, "Мотыльки", 0, QuestGoals.Kill, Giver, [new QuestTarget(1000, 10, done)], []);

    [Fact]
    public void NoLeaderChosen_Stops()
    {
        _settings.Follow.Leader = "";

        Tick();

        Assert.Contains("главный не выбран", _stopped);
    }

    [Fact]
    public void LeaderIsMyself_Stops()
    {
        _settings.Follow.Leader = "помощник";

        Tick();

        Assert.Contains("сам персонаж", _stopped);
    }

    [Fact]
    public void LeaderNotSeen_WaitsWithoutStopping()
    {
        _settings.Follow.Leader = "Другой";

        Tick();

        Assert.Null(_stopped);
        Assert.Empty(_actions.Calls);
        Assert.Contains("не вижу", Brain.Status);
    }

    [Fact]
    public void LeaderSnapshotStale_Waits()
    {
        _board.Publish(_leader.Snapshot());
        _me.Wait(10);

        Brain.Tick(_me.Snapshot());

        Assert.Empty(_actions.Calls);
        Assert.Contains("не вижу", Brain.Status);
    }

    [Fact]
    public void LeaderClose_StandsStill()
    {
        _leader.Position = new Position(4, 0, 0);

        Tick();

        Assert.Empty(_actions.Calls);
        Assert.Contains("рядом", Brain.Status);
    }

    [Fact]
    public void LeaderAway_WalksSmartHalfwayIntoDistance()
    {
        _leader.Position = new Position(30, 0, 0);

        Tick();

        Assert.Equal(["move (30,0; 0,0; h 0,0) умно"], _actions.Calls);
        Assert.Null(_stopped);
    }

    [Fact]
    public void LeaderMovesOn_WalksToNewPlace()
    {
        _leader.Position = new Position(30, 0, 0);
        Tick();
        _leader.Position = new Position(60, 0, 0);

        Tick(1.1);

        Assert.Equal(2, _actions.MovePoints.Count);
        Assert.Equal(60, _actions.MovePoints[1].X);
    }

    [Fact]
    public void LeaderTeleportedFar_Waits()
    {
        _leader.Position = new Position(5000, 0, 0);

        Tick();

        Assert.Empty(_actions.Calls);
        Assert.Contains("далеко", Brain.Status);
    }

    [Fact]
    public void LeaderTookOff_TakesOff()
    {
        _leader.Flying = true;
        _leader.Position = new Position(2, 300, 0);

        Tick();

        Assert.Equal(["fly-toggle"], _actions.Calls);
    }

    [Fact]
    public void LeaderTookOff_FlyOff_StaysOnGround()
    {
        _settings.Follow.Fly = false;
        _leader.Flying = true;
        _leader.Position = new Position(2, 300, 0);

        Tick();

        Assert.DoesNotContain("fly-toggle", _actions.Calls);
    }

    [Fact]
    public void LeaderLandedNearby_Lands()
    {
        _me.Flying = true;
        _me.Position = new Position(0, 100, 0);
        _leader.Position = new Position(3, 0, 0);

        Tick();

        Assert.Equal(["fly-toggle"], _actions.Calls);
    }

    [Fact]
    public void LeaderBeatsMob_SelectsSameMob()
    {
        var mob = _leader.AddMob(0x80200001, "Мотылёк", 6, hp: 50);
        _leader.Replace(mob, m => m with { MaxHp = 100 });
        _leader.TargetWid = mob.Wid;
        _me.AddMob(mob.Wid, "Мотылёк", 6);

        // Просьбу бою ассист ставит после него: выбирает бой на следующем снимке
        Tick();
        Tick();

        Assert.Equal([$"select {mob.Wid:X}"], _actions.Calls);
    }

    [Fact]
    public void LeaderOnlySelectedMob_DoesNotAttack()
    {
        var mob = _leader.AddMob(0x80200001, "Мотылёк", 6, hp: 100);
        _leader.Replace(mob, m => m with { MaxHp = 100 });
        _leader.TargetWid = mob.Wid;
        _me.AddMob(mob.Wid, "Мотылёк", 6);

        Tick();
        Tick();

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void MobAttacksLeader_HelpsHim()
    {
        var mob = _leader.AddMob(0x80200001, "Мотылёк", 6, targetWid: LeaderWid);
        _me.AddMob(mob.Wid, "Мотылёк", 6);

        // Просьбу бою ассист ставит после него: выбирает бой на следующем снимке
        Tick();
        Tick();

        Assert.Equal([$"select {mob.Wid:X}"], _actions.Calls);
    }

    [Fact]
    public void AssistOff_DoesNotHelp()
    {
        _settings.Follow.Assist = false;
        var mob = _leader.AddMob(0x80200001, "Мотылёк", 6, targetWid: LeaderWid);
        _me.AddMob(mob.Wid, "Мотылёк", 6);

        Tick();
        Tick();

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void DistanceIsClamped()
    {
        _settings.Follow.Distance = 1000;

        _settings.Normalize();

        Assert.Equal(FollowSettings.MaxDistance, _settings.Follow.Distance);
    }
}

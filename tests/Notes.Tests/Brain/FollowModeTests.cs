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
    private const int Portal = 167;

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
            _brain = BotModes.Create(BotMode.Follow, new ActionRunner(new GameControl(_actions), NullLogger.Instance), new ClassSkills { TownPortal = Portal },
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
    public void LeaderAway_WalksStraight()
    {
        _leader.Position = new Position(30, 0, 0);

        Tick();

        Assert.Equal(["move (30,0; 0,0; h 0,0)"], _actions.Calls);
        Assert.Null(_stopped);
    }

    [Fact]
    public void LeaderAway_ClientPath_WalksByAutopath()
    {
        _settings.Follow.Path = FollowPath.Client;
        _leader.Position = new Position(30, 0, 0);

        Tick();

        Assert.Equal(["move (30,0; 0,0; h 0,0) умно"], _actions.Calls);
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
    public void LeaderJumped_JumpsOnce()
    {
        _leader.Jumps = 1;

        Tick();
        Tick();

        Assert.Equal(["jump"], _actions.Calls.Where(c => c == "jump"));
    }

    [Fact]
    public void LeaderDoubleJumped_SecondJump()
    {
        _leader.Jumps = 1;
        Tick();
        _me.Jumps = 1;
        _leader.Jumps = 2;

        Tick();

        Assert.Equal(["jump", "jump"], _actions.Calls.Where(c => c == "jump"));
    }

    [Fact]
    public void LeaderJumped_JumpOff_StaysDown()
    {
        _settings.Follow.Jump = false;
        _leader.Jumps = 1;

        Tick();

        Assert.DoesNotContain("jump", _actions.Calls);
    }

    [Fact]
    public void BothOnGround_NoJump()
    {
        Tick();

        Assert.DoesNotContain("jump", _actions.Calls);
    }

    [Fact]
    public void LeaderCastsPortal_CastsToo_OnceAndHoldsTurn()
    {
        _me.AddSkill(Portal);
        _leader.CastingSkillId = Portal;
        _leader.Position = new Position(30, 0, 0);

        Tick();
        _me.CastingSkillId = Portal;
        Tick();

        Assert.Equal([$"apply {Portal} 0"], _actions.Calls);
    }

    [Fact]
    public void LeaderCastsPortal_MineOnCooldown_Stays()
    {
        _me.AddSkill(Portal, cooldownLeftMs: 600_000);
        _leader.CastingSkillId = Portal;

        Tick();

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("apply"));
    }

    private static readonly Position Bridge = new(-1703.7f, 219.6f, 97.8f);

    // Мастер телепортации в 2 м: строка 5 — «Посёлок у моста»
    private void AddTeleportMaster() => _me.Npcs.Add(new NpcInfo(0x80103F78, 0x80103F78, NpcKind.Npc, NpcState.Standing, 0,
        new Position(2, 0, 0), "Мастер телепортации", 0)
    {
        Tid = 2202,
        Teleports = [new TeleportPoint(2, 3701, "Портовый город", new Position(267.7f, 219.5f, 973.9f), 200),
                     new TeleportPoint(5, 4301, "Посёлок у моста", Bridge, 200)],
    });

    [Fact]
    public void LeaderTeleportedAtMaster_TeleportsToSamePoint()
    {
        AddTeleportMaster();
        _leader.Position = new Position(1, 0, 0);
        Tick();

        _leader.Position = Bridge;
        Tick();
        Tick(0.6);

        Assert.Equal(["npc-hello", "teleport 5"], _actions.Calls);
    }

    [Fact]
    public void LeaderJumpedElsewhere_NoTeleport()
    {
        AddTeleportMaster();
        _leader.Position = new Position(1, 0, 0);
        Tick();

        _leader.Position = new Position(3000, 0, 3000);
        Tick();
        Tick(0.6);

        Assert.Empty(_actions.Calls);
    }

    private static readonly Position Gorge = new(-132.7f, 218.7f, -309.4f);

    // Мастера рядом нет; в таблице игры — «Древнее ущелье», в сумке — идеальная руна
    private void AddRune()
    {
        _me.Waypoints.Add(new TeleportPoint(0, 4401, "Древнее ущелье", Gorge, 0));
        _me.Runes.Add(69449);
        _me.Bag.Add(new InventoryItem(5, 69449, 0, 10, null, null));
    }

    [Fact]
    public void LeaderTeleportedNoMaster_UsesRune()
    {
        AddRune();
        _leader.Position = new Position(1, 0, 0);
        Tick();

        _leader.Position = Gorge;
        Tick();

        Assert.Equal(["rune 69449 4401"], _actions.Calls);
    }

    [Fact]
    public void LeaderTeleportedNoMaster_NoRunes_Stays()
    {
        AddRune();
        _me.Bag.Clear();
        _leader.Position = new Position(1, 0, 0);
        Tick();

        _leader.Position = Gorge;
        Tick();

        Assert.DoesNotContain(_actions.Calls, call => call.StartsWith("rune"));
    }

    [Fact]
    public void LeaderTeleportedAtMaster_TeleportsAtMasterNotRune()
    {
        AddRune();
        _me.Waypoints.Add(new TeleportPoint(0, 4301, "Посёлок у моста", Bridge, 0));
        AddTeleportMaster();
        _leader.Position = new Position(1, 0, 0);
        Tick();

        _leader.Position = Bridge;
        Tick();
        Tick(0.6);

        Assert.Equal(["npc-hello", "teleport 5"], _actions.Calls);
    }

    [Fact]
    public void LeaderTeleported_TeleportOff_Stays()
    {
        _settings.Follow.Teleport = false;
        AddTeleportMaster();
        _leader.Position = new Position(1, 0, 0);
        Tick();

        _leader.Position = Bridge;
        Tick();
        Tick(0.6);

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void LeaderCastsPortal_PortalOff_Stays()
    {
        _settings.Follow.Portal = false;
        _me.AddSkill(Portal);
        _leader.CastingSkillId = Portal;

        Tick();

        Assert.DoesNotContain(_actions.Calls, c => c.StartsWith("apply"));
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
    public void LeaderBeatsMobFarFromStart_StillAssists()
    {
        // Радиус фарма — про фарм: за главным ушли далеко от места «Старт», его моба всё равно бьём
        _settings.Target.FarmRadius = 30;
        Tick();
        _me.Position = _leader.Position = new Position(200, 0, 0);
        var mob = _leader.AddMob(0x80200001, "Мотылёк", 206, hp: 50);
        _leader.Replace(mob, m => m with { MaxHp = 100 });
        _leader.TargetWid = mob.Wid;
        _me.AddMob(mob.Wid, "Мотылёк", 206);

        Tick();
        Tick();

        Assert.Contains($"select {mob.Wid:X}", _actions.Calls);
    }

    [Fact]
    public void PetNotSummoned_CallsAgainHere_NotToStart()
    {
        // Центра фарма у «Следовать» нет: пет не призвался — зовём снова там, где стоим, к месту «Старт» не идём
        _settings.Pet.Enabled = true;
        _me.SetPets(null, new PetInCage(1, 1, 0) { Name = "Скорпион", Habitat = Habitat.Ground });
        Tick();
        Assert.Equal(["summon 1"], _actions.Calls);

        _me.Position = _leader.Position = new Position(20, 0, 0);
        Tick(9); // призыв не подтвердился за 8 с
        Tick();
        Tick();

        Assert.DoesNotContain(_actions.Calls, call => call.StartsWith("move"));
        Assert.Equal("summon 1", _actions.Calls.Last());
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
    public void LeaderTookQuest_TakesSameFromNpcNearby()
    {
        AddNpc(_me, Giver, 3);
        _me.QuestCandidates = [new QuestInfo(Task, "Мотыльки", 0, QuestGoals.Kill, Giver, [], []) { GiverNpc = Giver }];
        _leader.Quests = [Quest()];

        Tick();
        Assert.Equal(["npc-hello"], _actions.Calls);
        Tick(1.1);
        Assert.Equal(["npc-hello", $"accept-task {Task}"], _actions.Calls);

        _me.Quests = [Quest()];
        Tick();
        Assert.Equal(["npc-hello", $"accept-task {Task}", "npc-back"], _actions.Calls);
    }

    [Fact]
    public void LeaderTookQuest_GameSaysNo_DoesNotGo()
    {
        AddNpc(_me, Giver, 3);
        _me.QuestCandidates = [new QuestInfo(Task, "Мотыльки", 0, QuestGoals.Kill, Giver, [], []) { GiverNpc = Giver }];
        _actions.QuestCheckAnswers[Task] = 5;
        _leader.Quests = [Quest()];

        Tick();
        Tick();

        Assert.DoesNotContain("npc-hello", _actions.Calls);
    }

    [Fact]
    public void QuestsOff_DoesNotTake()
    {
        _settings.Follow.Quests = false;
        AddNpc(_me, Giver, 3);
        _me.QuestCandidates = [new QuestInfo(Task, "Мотыльки", 0, QuestGoals.Kill, Giver, [], []) { GiverNpc = Giver }];
        _leader.Quests = [Quest()];

        Tick();

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void LeaderHandedIn_HandsInDoneQuest()
    {
        AddNpc(_me, Giver, 3);
        _me.Quests = [Quest(done: 10)];
        _leader.Quests = [Quest(done: 10)];
        Tick();
        Assert.Empty(_actions.Calls);

        _leader.Quests = [];
        Tick();
        Assert.Equal(["npc-hello"], _actions.Calls);
        Tick(1.1);
        Assert.Equal(["npc-hello", $"return-task {Task}"], _actions.Calls);

        _me.Quests = [];
        Tick();
        Assert.Equal(["npc-hello", $"return-task {Task}", "npc-back"], _actions.Calls);
    }

    [Fact]
    public void LeaderHandedIn_MineNotDone_BeatsQuestMobNearby()
    {
        AddNpc(_me, Giver, 3);
        _me.Quests = [Quest(done: 3)];
        _leader.Quests = [Quest(done: 10)];
        Tick();

        _leader.Quests = [];
        var mob = new NpcInfo(0x80200002, 0x80200002, NpcKind.Mob, NpcState.Standing, 0, new Position(10, 0, 0), "Мотылёк", 0) { Tid = 1000 };
        _me.Npcs.Add(mob);
        Tick();
        Tick();

        Assert.DoesNotContain($"return-task {Task}", _actions.Calls);
        Assert.Contains($"select {mob.Wid:X}", _actions.Calls);
    }

    // «Триумф»: 2 награды на выбор, как в игре
    private static QuestInfo Triumph() => new QuestInfo(1179, "Триумф", 0, QuestGoals.Talk, Giver, [], [])
    {
        RewardChoices = [[new RewardItem(12498, 1), new RewardItem(8617, 5)], [new RewardItem(12501, 1), new RewardItem(8647, 5)]],
    };

    [Fact]
    public void Board_LearnsLeadersRewardChoiceFromHisBag()
    {
        _leader.Quests = [Triumph()];
        _board.Publish(_leader.Snapshot());

        _leader.Quests = [];
        _board.Publish(_leader.Wait(0.25).Snapshot());
        Assert.Null(_board.RewardChoice("Главный", 1179));

        _leader.Bag.Add(new InventoryItem(1, 12501, 0, 1, null, null));
        _leader.Bag.Add(new InventoryItem(2, 8647, 9, 5, null, null));
        _board.Publish(_leader.Wait(0.25).Snapshot());

        Assert.Equal(1, _board.RewardChoice("главный", 1179));
    }

    [Fact]
    public void Board_RewardNeverCame_ChoiceUnknown()
    {
        _leader.Quests = [Triumph()];
        _board.Publish(_leader.Snapshot());
        _leader.Quests = [];
        _board.Publish(_leader.Wait(0.25).Snapshot());
        _board.Publish(_leader.Wait(11).Snapshot());

        _leader.Bag.Add(new InventoryItem(1, 12501, 0, 1, null, null));
        _leader.Bag.Add(new InventoryItem(2, 8647, 9, 5, null, null));
        _board.Publish(_leader.Wait(0.25).Snapshot());

        Assert.Null(_board.RewardChoice("Главный", 1179));
    }

    [Fact]
    public void LeaderHandedInWithChoice_HandsInSameReward()
    {
        AddNpc(_me, Giver, 3);
        _me.Quests = [Triumph()];
        _leader.Quests = [Triumph()];
        Tick();

        _leader.Quests = [];
        Tick();
        Assert.Empty(_actions.Calls);
        Assert.Contains("какую награду", Brain.Status);

        _leader.Bag.Add(new InventoryItem(1, 12501, 0, 1, null, null));
        _leader.Bag.Add(new InventoryItem(2, 8647, 9, 5, null, null));
        Tick();
        Tick(1.1);

        Assert.Equal(["npc-hello", "return-task 1179 награда 1"], _actions.Calls);
    }

    [Fact]
    public void LeaderHandedInWithChoice_Unknown_DoesNotHandIn()
    {
        AddNpc(_me, Giver, 3);
        _me.Quests = [Triumph()];
        _leader.Quests = [Triumph()];
        Tick();

        _leader.Quests = [];
        for (var i = 0; i < 20; i++)
            Tick(1);

        Assert.Empty(_actions.Calls);
        Assert.Null(_stopped);
    }

    private const uint Teacher = 4000;
    // Не атакующий скилл по умолчанию (тот у ведомого уже есть)
    private const int Swarm = 330;

    // Главный учится у учителя рядом: был один скилл, появился «Исцеление питомца» (или вырос уровень)
    private void LeaderLearns(int level)
    {
        if (_leader.Skills.Count == 0)
            _leader.AddSkill(167);
        if (!_leader.Npcs.Any(n => n.Tid == Teacher))
            AddNpc(_leader, Teacher, 3);
        _board.Publish(_leader.Snapshot());
        _leader.Skills.RemoveAll(s => s.Id == Swarm);
        _leader.Skills.Add(new SkillInfo(Swarm, level, 0, 3000, 0, "Исцеление питомца"));
        _board.Publish(_leader.Wait(0.25).Snapshot());
    }

    [Fact]
    public void Board_RemembersTeacherWhenSkillLearned()
    {
        LeaderLearns(1);

        Assert.Equal((int)Teacher, _board.Teacher("Главный", Swarm));
    }

    [Fact]
    public void Board_SkillWithoutNpcNearby_TeacherUnknown()
    {
        _leader.AddSkill(167);
        _board.Publish(_leader.Snapshot());
        _leader.AddSkill(Swarm);
        _board.Publish(_leader.Wait(0.25).Snapshot());

        Assert.Null(_board.Teacher("Главный", Swarm));
    }

    [Fact]
    public void LeaderLearnedSkill_LearnsAtSameTeacher()
    {
        LeaderLearns(1);
        AddNpc(_me, Teacher, 3);

        Tick();
        Assert.Equal(["npc-hello"], _actions.Calls);
        Tick(1.1);
        Assert.Equal(["npc-hello", $"learn-skill {Swarm}"], _actions.Calls);

        _me.Skills.Add(new SkillInfo(Swarm, 1, 0, 3000, 0, "Исцеление питомца"));
        Tick();
        Assert.Equal(["npc-hello", $"learn-skill {Swarm}", "npc-back"], _actions.Calls);
    }

    [Fact]
    public void LeaderRaisedSkill_RaisesLevelByLevel()
    {
        _me.Skills.Add(new SkillInfo(Swarm, 1, 0, 3000, 0, "Исцеление питомца"));
        LeaderLearns(3);
        AddNpc(_me, Teacher, 3);

        Tick();
        Tick(1.1);
        _me.Skills[_me.Skills.Count - 1] = new SkillInfo(Swarm, 2, 0, 3000, 0, "Исцеление питомца");
        Tick();
        Tick();
        Tick(1.1);

        Assert.Equal(2, _actions.Calls.Count(x => x == $"learn-skill {Swarm}"));
    }

    [Fact]
    public void TeacherRefuses_GivesUpUntilLeaderGoesHigher()
    {
        LeaderLearns(1);
        AddNpc(_me, Teacher, 3);

        for (var i = 0; i < 40; i++)
            Tick(1);

        Assert.Equal(3, _actions.Calls.Count(x => x == $"learn-skill {Swarm}"));
        Assert.Null(_stopped);
    }

    [Fact]
    public void SkillsOff_DoesNotLearn()
    {
        _settings.Follow.Skills = false;
        LeaderLearns(1);
        AddNpc(_me, Teacher, 3);

        Tick();

        Assert.Empty(_actions.Calls);
    }

    // «Призыв дрессировщицы» 5909 → «Верный друг» 5912: у главного уже следующая цепочка «Умение управления» 5910
    [Fact]
    public void LeaderPassedChain_HandsInMyDoneSubquestEvenIfNeverSeenAtLeader()
    {
        const uint Trainer = 11534;
        AddNpc(_me, Trainer, 2);
        _me.Quests =
        [
            new QuestInfo(5909, "Призыв дрессировщицы", 0, QuestGoals.Parts, 0, [], []),
            new QuestInfo(5912, "Верный друг", 5909, QuestGoals.Talk, Trainer, [], []),
        ];
        _leader.Quests = [new QuestInfo(5910, "Умение управления", 0, QuestGoals.Parts, 0, [], [])];

        Tick();
        Tick(1.1);

        Assert.Equal(["npc-hello", "return-task 5912"], _actions.Calls);
    }

    // «Общее дело» 14: главный сдал подзадание «Необходимые материалы» 120 и взял следующее «Разговор со швеей» 127
    [Fact]
    public void LeaderHandedSubquestAndMovedOn_HandsInMine()
    {
        const uint Elder = 2192;
        AddNpc(_me, Elder, 2);
        var chain = new QuestInfo(14, "Общее дело", 0, QuestGoals.Parts, 0, [], []);
        var materials = new QuestInfo(120, "Необходимые материалы", 14, QuestGoals.Talk, Elder, [], []);
        _me.Quests = [chain, materials];
        _leader.Quests = [chain, materials];
        Tick();
        Assert.Empty(_actions.Calls);

        _leader.Quests = [chain, new QuestInfo(127, "Разговор со швеей", 14, QuestGoals.Talk, 2194, [], [])];
        Tick();
        Tick(1.1);

        Assert.Equal(["npc-hello", "return-task 120"], _actions.Calls);
    }

    [Fact]
    public void LeaderStillOnChain_DoesNotHandIn()
    {
        AddNpc(_me, Giver, 2);
        _me.Quests = [new QuestInfo(5909, "Призыв", 0, QuestGoals.Parts, 0, [], []), new QuestInfo(5912, "Верный друг", 5909, QuestGoals.Talk, Giver, [], [])];
        _leader.Quests = [new QuestInfo(5909, "Призыв", 0, QuestGoals.Parts, 0, [], []), new QuestInfo(5911, "Друг", 5909, QuestGoals.Talk, Giver, [], [])];

        Tick();

        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void GameRefusedQuest_ChecksAgainLater()
    {
        AddNpc(_me, Giver, 3);
        _me.QuestCandidates = [new QuestInfo(Task, "Мотыльки", 0, QuestGoals.Kill, Giver, [], []) { GiverNpc = Giver }];
        _actions.QuestCheckAnswers[Task] = 15;
        _leader.Quests = [Quest()];
        Tick();
        Assert.Empty(_actions.Calls);

        _actions.QuestCheckAnswers.Remove(Task);
        Tick(31);

        Assert.Equal(["npc-hello"], _actions.Calls);
    }

    [Fact]
    public void ChooseOneParent_TakesSubquestNotParent()
    {
        const uint Trainer = 11534;
        AddNpc(_me, Trainer, 3);
        var wolf = new QuestInfo(5913, "Свирепый волк", 5910, QuestGoals.Kill, Trainer, [], []) { GiverNpc = Trainer };
        _me.QuestCandidates = [new QuestInfo(5910, "Умение управления", 0, QuestGoals.Parts, 0, [], []) { GiverNpc = Trainer, Options = [wolf] }];
        _leader.Quests = [new QuestInfo(5910, "Умение управления", 0, QuestGoals.Parts, 0, [], []), wolf];

        Tick();
        Tick(1.1);

        Assert.Equal(["npc-hello", "accept-task 5913"], _actions.Calls);
    }

    [Fact]
    public void AsksLeaderTabToReadQuests()
    {
        Tick();

        Assert.True(_board.QuestsWanted("Главный", _me.Time));
    }

    [Fact]
    public void DistanceIsClamped()
    {
        _settings.Follow.Distance = 1000;

        _settings.Normalize();

        Assert.Equal(FollowSettings.MaxDistance, _settings.Follow.Distance);
    }
}

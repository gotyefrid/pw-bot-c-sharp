using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.GameFiles;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>«Прокачать друида»: взять первое задание, бить мобов цели издалека, ходить «умно», сдавать и закрывать окно NPC.</summary>
public class QuestBehaviorTests
{
    private const int StartTask = 1177;
    private const uint Mentor = 3518;
    private const uint Elder = 2206;
    private const uint Moth = 1000;

    // «Рудокоп» — в списке «не брать» по умолчанию: первым берётся «Первое приключение»
    private readonly FakeWorld _world = new()
    {
        Quests = [],
        AvailableQuests =
        [
            new QuestInfo(64250, "Рудокоп", 0, QuestGoals.Parts, 0, [], []) { GiverNpc = 2222 },
            new QuestInfo(StartTask, "Первое приключение", 0, QuestGoals.Parts, 0, [], []) { GiverNpc = Mentor },
        ],
    };
    private readonly FakeActions _actions = new();
    private readonly BotSettings _settings = new();
    private readonly WorldRequests _requests = new();
    private readonly Dictionary<int, List<Position>> _points = [];
    private string? _stopped;
    private IBotRunner? _brain;

    public QuestBehaviorTests()
    {
        _settings.Pet.Enabled = false;
        _settings.Potions.MpBelow = 0;
        _world.AddSkill(_settings.Combat.AttackSkillId);
    }

    private IBotRunner Brain
    {
        get
        {
            if (_brain is not null)
                return _brain;

            _brain = BotModes.Create(BotMode.LevelUp, new ActionRunner(new GameControl(_actions, requests: _requests), NullLogger.Instance),
                new ClassSkills(), _settings, NullLogger.Instance, random: new Random(1),
                quests: new QuestGuide(new GuidePoints(_points)));
            _brain.StopRequested += reason => _stopped = reason;
            return _brain;
        }
    }

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    private NpcInfo AddNpc(uint wid, uint tid, string name, float distance)
    {
        var npc = new NpcInfo(wid, wid, NpcKind.Npc, NpcState.Standing, 0, new Position(distance, 0, 0), name, 0) { Tid = (int)tid };
        _world.Npcs.Add(npc);
        return npc;
    }

    private NpcInfo AddMob(uint wid, uint tid, string name, float distance)
    {
        var mob = new NpcInfo(wid, wid, NpcKind.Mob, NpcState.Standing, 0, new Position(distance, 0, 0), name, 0) { Tid = (int)tid };
        _world.Npcs.Add(mob);
        return mob;
    }

    private static QuestInfo KillMoths(int done, uint awardNpc = Elder)
        => new(1204, "Посетить Город Оборотней", 0, QuestGoals.Kill, awardNpc, [new QuestTarget(Moth, 10, done)], []);

    [Fact]
    public void AsksGameToReadQuests()
    {
        Tick();

        Assert.True(_requests.Quests);
    }

    [Fact]
    public void NoQuests_WalksSmartToMentor()
    {
        AddNpc(0x80103F91, Mentor, "Наставница", 30);

        Tick();

        // Встаёт в 2,5 м от NPC, «умно»
        Assert.Equal(["move (27,5; 0,0; h 0,0) умно"], _actions.Calls);
    }

    [Fact]
    public void NoQuestsMentorNotSeen_WalksToHerGuidePoint()
    {
        _points[(int)Mentor] = [new Position(100, 5, 40)];

        Tick();

        Assert.Equal(["move (100,0; 40,0; h 5,0) умно"], _actions.Calls);
    }

    [Fact]
    public void MentorNear_SaysHelloTakesFirstQuestAndClosesWindow()
    {
        AddNpc(0x80103F91, Mentor, "Наставница", 3);

        Tick();
        Assert.Equal(["npc-hello"], _actions.Calls);

        // Окно открывает ответ сервера — берём через секунду
        Tick(0.5);
        Assert.Equal(["npc-hello"], _actions.Calls);
        Tick(0.6);
        Assert.Equal(["npc-hello", "accept-task 1177"], _actions.Calls);

        _world.Quests = [new QuestInfo(StartTask, "Первое приключение", 0, QuestGoals.Parts, 0, [], [])];
        Tick();
        Assert.Equal(["npc-hello", "accept-task 1177", "npc-back"], _actions.Calls);
    }

    [Fact]
    public void QuestMobInRange_SelectsThenCastsWithoutApproach()
    {
        _world.Quests = [KillMoths(3)];
        var moth = AddMob(0x80103FA0, Moth, "Малахитовый мотыль", 12);
        AddMob(0x80103E72, 3303, "Малая росянка", 5);

        Tick();
        Assert.Equal([$"select {moth.Wid:X}"], _actions.Calls);

        _world.TargetWid = moth.Wid;
        Tick();
        // Пакетом по WID — без подхода «как кнопкой»
        Assert.Equal($"cast {_settings.Combat.AttackSkillId} {moth.Wid:X}", _actions.Calls.Last());
    }

    [Fact]
    public void QuestMobTooFar_WalksSmartIntoCastRange()
    {
        _world.Quests = [KillMoths(3)];
        var moth = AddMob(0x80103FA0, Moth, "Малахитовый мотыль", 40);
        _world.TargetWid = moth.Wid;

        Tick();

        Assert.Equal(["move (25,0; 0,0; h 0,0) умно"], _actions.Calls);
    }

    [Fact]
    public void NoQuestMobAround_GoesWhereTheyLive()
    {
        _world.Quests = [KillMoths(3)];
        _points[(int)Moth] = [new Position(300, 0, 0), new Position(120, 0, 0)];

        Tick();

        Assert.Equal(["move (120,0; 0,0; h 0,0) умно"], _actions.Calls);
    }

    [Fact]
    public void KillsDone_ReturnsQuestToNpcAndClosesWindow()
    {
        _world.Quests = [KillMoths(10)];
        AddNpc(0x801000FD, Elder, "Старейшина Города Оборотней", 2);

        Tick();
        Tick(1.1);
        Assert.Equal(["npc-hello", "return-task 1204"], _actions.Calls);

        _world.Quests = [];
        Tick();
        Assert.Equal(["npc-hello", "return-task 1204", "npc-back"], _actions.Calls);
    }

    [Fact]
    public void KillsDoneNobodyToReturn_WaitsForGame()
    {
        _world.Quests = [KillMoths(10, awardNpc: 0)];

        Tick();
        Tick();

        Assert.Empty(_actions.Calls);
        Assert.Null(_stopped);
    }

    [Fact]
    public void NpcDoesNotTakeQuest_TriesAgainThenStops()
    {
        _world.Quests = [KillMoths(10)];
        AddNpc(0x801000FD, Elder, "Старейшина Города Оборотней", 2);

        for (var i = 0; i < 3; i++)
        {
            Tick();
            Tick(1.1);
            Tick(4.1);
        }

        Assert.Equal(3, _actions.Calls.Count(c => c == "return-task 1204"));
        Assert.NotNull(_stopped);
    }

    [Fact]
    public void NoAvailableQuestsOnServer_StopsAskingToTakeOneInGame()
    {
        _world.AvailableQuests = null;

        Tick();

        Assert.Contains("возьмите задание в игре", _stopped);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void OnlySkippedQuestsAvailable_Stops()
    {
        _settings.Quests.SkipNames.Add("Первое приключение");

        Tick();

        Assert.Contains("закончились", _stopped);
        Assert.Empty(_actions.Calls);
    }

    [Fact]
    public void NpcDoesNotGiveQuest_SkipsItAndTakesNext()
    {
        _world.AvailableQuests!.Add(new QuestInfo(5910, "Умение управления", 0, QuestGoals.Parts, 0, [], []) { GiverNpc = 11534 });
        AddNpc(0x80103F91, Mentor, "Наставница", 3);
        AddNpc(0x80104103, 11534, "Дрессировщик Лисюэ", 3);

        for (var i = 0; i < 3; i++)
        {
            Tick();
            Tick(1.1);
            Tick(4.1);
        }
        Tick();
        Tick(1.1);

        Assert.Equal(3, _actions.Calls.Count(c => c == "accept-task 1177"));
        Assert.Equal("accept-task 5910", _actions.Calls.Last());
        Assert.Null(_stopped);
    }

    [Fact]
    public void CollectGoal_StopsWithReason()
    {
        _world.Quests = [new QuestInfo(500, "Собрать травы", 0, QuestGoals.Collect, Elder, [], [new QuestTarget(3098, 5, null)])];

        Tick();

        Assert.Contains("Собрать травы", _stopped);
    }
}

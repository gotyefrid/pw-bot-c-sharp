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
using Newtonsoft.Json.Linq;
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
    private readonly Dictionary<int, QuestScript> _scripts = [];
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
                quests: new QuestGuide(new GuidePoints(_points), _scripts));
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

    // Как с кузнецом в игре: стоим над NPC (2 м по карте), а он на 10 м ниже — сервер не слышит («Служба недоступна»)
    [Fact]
    public void MentorNearOnMapButFarBelow_WalksThenStops()
    {
        var npc = AddNpc(0x80103F91, Mentor, "Наставница", 2);
        _world.Replace(npc, n => n with { Position = n.Position with { Height = -10 } });

        Tick();
        Assert.DoesNotContain("npc-hello", _actions.Calls);

        for (var i = 0; i < 50 && _stopped is null; i++)
            Tick();
        Assert.DoesNotContain("npc-hello", _actions.Calls);
        Assert.Contains("ниже на 10 м", _stopped);
    }

    [Fact]
    public void QuestMobHighAbove_WalksCloser()
    {
        // 15 м по карте, 12 м выше — 19 м по прямой, дальше 18
        _world.Quests = [KillMoths(3)];
        var moth = AddMob(0x80103FA0, Moth, "Малахитовый мотыль", 15);
        _world.Replace(moth, m => m with { Position = m.Position with { Height = 12 } });
        _world.TargetWid = moth.Wid;

        Tick();

        Assert.StartsWith("move", _actions.Calls.Single());
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
    public void ReturnedQuestWithScript_LearnsMissingSkillsHatchesEggThenClosesWindow()
    {
        const uint Trainer = 11534;
        _scripts[5913] = new QuestScript
        {
            After =
            [
                JObject.Parse("""{ "kind": "LearnSkill", "skill": 330 }"""),
                JObject.Parse("""{ "kind": "LearnSkill", "skill": 329 }"""),
                JObject.Parse("""{ "kind": "HatchPets" }"""),
            ],
        };
        _world.AddSkill(329);
        _world.Quests = [new QuestInfo(5913, "Свирепый волк", 5910, QuestGoals.Kill, Trainer, [new QuestTarget(1047, 0, 0)], [])];
        AddNpc(0x80104103, Trainer, "Дрессировщик Лисюэ", 3);

        Tick();
        Tick(1.1);
        _world.Quests = [];
        _world.Bag.Add(new InventoryItem(3, 10712, 0x1A, 1, null, null) { IsPetEgg = true, Name = "Яйцо Молодого свирепого волка" });
        Tick();
        Assert.Equal(["npc-hello", "return-task 5913"], _actions.Calls);

        Tick(1.6);
        Tick(1.6);
        Tick(1.6);
        Tick(1.6);

        // 329 уже есть — не учим; окно закрыто после всех шагов
        Assert.Equal(["npc-hello", "return-task 5913", "learn-skill 330", "hatch-pet 3 10712", "npc-back"], _actions.Calls);
    }

    [Fact]
    public void ScriptSkillNotLearned_AsksOnceThenClosesWindow()
    {
        _scripts[1204] = new QuestScript { After = [JObject.Parse("""{ "kind": "LearnSkill", "skill": 330 }""")] };
        _world.Quests = [KillMoths(10)];
        AddNpc(0x801000FD, Elder, "Старейшина Города Оборотней", 2);

        Tick();
        Tick(1.1);
        _world.Quests = [];
        for (var i = 0; i < 5; i++)
            Tick(1.6);

        Assert.Equal(["npc-hello", "return-task 1204", "learn-skill 330", "npc-back"], _actions.Calls);
    }

    private const uint Trainer = 11534;

    private static QuestInfo Feeding() => new(5937, "Кормление питомцев", 0, QuestGoals.Instant, 0, [], []) { GiverNpc = Trainer };

    [Fact]
    public void Candidates_TakesFirstOneTheGameAllows()
    {
        // «Кормление питомцев» уже выполнено — игра его не даёт (в сохранённом списке игры оно ещё есть)
        _world.QuestCandidates = [Feeding(), new QuestInfo(468, "Тлеющие огоньки", 0, QuestGoals.Parts, 0, [], []) { GiverNpc = 2290 }];
        _actions.QuestCheckAnswers[5937] = 10;
        AddNpc(0x80104103, Trainer, "Дрессировщик Лисюэ", 3);
        AddNpc(0x80104200, 2290, "Староста", 3);

        Tick();
        Tick(1.1);

        Assert.Equal(["npc-hello", "accept-task 468"], _actions.Calls);
    }

    [Fact]
    public void InstantQuest_CountedDoneWhenGameStopsGivingIt()
    {
        _world.QuestCandidates = [Feeding()];
        AddNpc(0x80104103, Trainer, "Дрессировщик Лисюэ", 3);

        Tick();
        Tick(1.1);
        // Награда пришла, в журнал не попало; игра больше не даёт
        _actions.QuestCheckAnswers[5937] = 10;
        Tick(1.1);
        Tick();

        Assert.Equal(["npc-hello", "accept-task 5937", "npc-back"], _actions.Calls);
        Assert.Contains("закончились", _stopped);
    }

    [Fact]
    public void ChoiceIsCheckedOnceWhileWalkingToGiver()
    {
        _world.QuestCandidates = [Feeding()];
        _points[(int)Trainer] = [new Position(100, 0, 0)];

        Tick();
        Tick();
        Tick();

        Assert.Equal(1, _actions.QuestCheckCalls);
    }

    private static QuestInfo Wolf() => new(5913, "Свирепый волк", 5910, QuestGoals.Kill, Trainer,
        [new QuestTarget(1047, 0, 0) { DropItem = 11531, DropNeed = 1 }], []) { GiverNpc = Trainer };

    [Fact]
    public void ChooseOneQuest_TakesOptionTheGameAllows()
    {
        var scorpion = new QuestInfo(5914, "Узорчатый скорпион", 5910, QuestGoals.Kill, Trainer, [], []);
        _world.QuestCandidates =
        [
            new QuestInfo(5910, "Умение управления", 0, QuestGoals.Parts, 0, [], []) { GiverNpc = Trainer, Options = [Wolf(), scorpion] },
        ];
        _actions.QuestCheckAnswers[5913] = 9;
        AddNpc(0x80104103, Trainer, "Дрессировщик Лисюэ", 3);

        Tick();
        Tick(1.1);

        Assert.Equal(["npc-hello", "accept-task 5914"], _actions.Calls);
    }

    [Fact]
    public void DropQuest_HuntsUntilItemInQuestBag()
    {
        _world.Quests = [Wolf()];
        _world.QuestItems = [];
        var wolf = AddMob(0x80103F1C, 1047, "Молодой свирепый волк", 10);

        Tick();
        Assert.Equal([$"select {wolf.Wid:X}"], _actions.Calls);

        _actions.Calls.Clear();
        _world.Npcs.Remove(wolf);
        _world.QuestItems = [new InventoryItem(0, 11531, 0, 1, null, null)];
        AddNpc(0x80104103, Trainer, "Дрессировщик Лисюэ", 3);
        Tick();
        Tick(1.1);

        Assert.Equal(["npc-hello", "return-task 5913"], _actions.Calls);
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

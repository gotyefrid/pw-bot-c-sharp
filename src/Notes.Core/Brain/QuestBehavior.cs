using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain.Quests;
using Notes.Core.GameFiles;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>Что нужно заданиям, кроме снимка: где водятся мобы и стоят NPC, что сделать у NPC после сдачи.</summary>
/// <param name="Points">Опорные точки по tid (configs\coord_data.txt).</param>
/// <param name="Scripts">Номер задания → скрипт: шаги сверх обычного (профиль сервера); null — нет.</param>
public sealed record QuestGuide(GuidePoints Points, IReadOnlyDictionary<int, QuestScript>? Scripts = null);

/// <summary>
/// «Прокачать друида»: делает принятые задания по журналу. Убить — бьёт мобов цели скиллом издалека (без подхода «как кнопкой»):
/// видит такого — выбирает и бьёт, далеко — подходит «умно» на дальность скилла, нет рядом — идёт к местам, где они водятся. Цель
/// выполнена или задание «поговорить» — идёт «умно» к NPC, заговаривает, сдаёт и закрывает окно. Следующее задание игра выдаёт
/// сама; принятых нет — берёт первое из «Доступных», которого нет в списке «не брать» (<see cref="Settings.QuestSettings"/>), у
/// NPC, который его даёт; «Доступные» — как у окна игры: задания по уровню и проверка игры «можно взять». Брать нечего — встаёт:
/// задания закончились. Задание «выполняется при взятии» (в журнал не попадает) — засчитывает, когда игра перестала его давать.
/// «Выполнить одно из подзаданий» — берёт подзадание, которое разрешает игра. Убить ради предмета — бьёт, пока предметов в сумке
/// заданий не хватает. У задания со скриптом (<see cref="QuestScript"/>) после
/// сдачи в том же разговоре выполняет шаги «after» (<see cref="QuestStep"/>: выучить скилл, вывести питомца…), потом закрывает окно.
/// «Собрать» и прочие цели не умеет — встаёт с причиной.
/// </summary>
public sealed class QuestBehavior : IBehavior
{
    // С какого расстояния NPC слышит разговор (сервер отказывает дальше ~5 м)
    private const float TalkDistance = 4f;
    // Куда встать у NPC: не упираться в него
    private const float NpcStop = 2.5f;
    // С какого расстояния бьём скиллом; дальше — подходим до CastRange - 3
    private const float CastRange = 18f;
    // Моба цели ищем в этом радиусе; дальше — идём к местам, где они водятся
    private const float SeeRange = 60f;
    // Дошли до места, где водятся, — точка «осмотрена», дальше к следующей
    private const float VisitedRadius = 8f;
    // После «привет» окно NPC открывает ответ сервера
    private static readonly TimeSpan DialogWait = TimeSpan.FromSeconds(1);
    // После «взять/сдать» список заданий обновляется ответом сервера
    private static readonly TimeSpan DealWait = TimeSpan.FromSeconds(4);
    // Одного моба бьём не дольше: застрял за камнем, не достать
    private static readonly TimeSpan MobLimit = TimeSpan.FromSeconds(40);
    // Перед шагами после сдачи: награда (учебники, яйцо) доходит до сумки
    private static readonly TimeSpan RewardWait = TimeSpan.FromSeconds(1.5);
    private const int DealTries = 3;

    private enum Stage { Idle, Hello, Deal, After }

    private readonly QuestGuide _guide;
    private readonly HashSet<uint> _skip = [];
    private readonly List<Position> _visited = [];

    private Stage _stage;
    private DateTime _stageAt;
    private int _dealTask;
    private bool _dealAccept;
    private int _tries;
    // Не дал (уже брали, не подходим) — больше не просить в этот запуск
    private readonly HashSet<int> _refused = [];
    // Какое задание идём брать: выбрано по проверке игры, пока не взяли или не отказались
    private QuestInfo? _offer;
    // Шаги скрипта после сдачи — по порядку, первый — текущий
    private readonly Queue<QuestStep> _steps = new();
    private uint _mobWid;
    private DateTime _mobSince;

    public QuestBehavior(QuestGuide guide) => _guide = guide;

    public string Name => "задания";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        c.Executor.Control.Requests.Quests = true;
        var quests = c.World.Quests;
        if (quests is null)
        {
            Status = "задания: читаю журнал";
            return false;
        }

        if (c.Runner.IsPending(ActionSlot.Bank))
        {
            Status = "задания: жду ответа NPC";
            return true;
        }

        if (_stage != Stage.Idle)
            return Dialog(c, quests);

        var quest = quests.FirstOrDefault(q => q.Goal != QuestGoals.Parts);
        if (quest is null)
        {
            if (quests.Count > 0)
                return c.RequestStop($"Задание «{quests[0].Name}» без цели, которую я понимаю — останавливаюсь");
            if (c.World.QuestCandidates is null && c.World.AvailableQuests is null)
                return c.RequestStop("Не вижу доступных заданий (на этом сервере не найдены) — возьмите задание в игре и нажмите «Старт»");
            var next = _offer ??= NextOffer(c);
            if (next is null)
                return c.RequestStop("Задания закончились: в «Доступных» только те, что в списке «не брать»");
            return Visit(c, next.GiverNpc, next.Id, accept: true, $"взять «{next.Name}»");
        }

        switch (quest.Goal)
        {
            case QuestGoals.Kill:
                var left = quest.Monsters.Where(m => !Enough(c.World, m)).ToList();
                if (left.Count > 0)
                    return Hunt(c, quest, left);
                if (quest.AwardNpc == 0)
                {
                    // Сдавать некому — игра засчитает сама и выдаст следующее
                    Status = $"задание «{quest.Name}»: выполнено, жду следующее";
                    return false;
                }
                return Visit(c, quest.AwardNpc, quest.Id, accept: false, $"сдать «{quest.Name}»");

            case QuestGoals.Talk when quest.AwardNpc != 0:
                return Visit(c, quest.AwardNpc, quest.Id, accept: false, $"сдать «{quest.Name}»");

            default:
                return c.RequestStop($"Задание «{quest.Name}»: цель «{QuestGoals.Text(quest.Goal)}» пока не умею — останавливаюсь");
        }
    }

    // Бить мобов цели: выбранный — скиллом издалека; иначе выбрать ближайшего; нет рядом — к местам, где водятся
    private bool Hunt(BrainContext c, QuestInfo quest, List<QuestTarget> left)
    {
        var w = c.World;
        var tids = new HashSet<uint>(left.Select(t => t.Tid));
        Status = $"задание «{quest.Name}»: {string.Join(", ", left.Select(t => $"{MobName(w, t.Tid)} {Progress(w, t)}"))}";

        if (w.Target is { Kind: NpcKind.Mob, IsDead: false } target && tids.Contains((uint)target.Tid) && !_skip.Contains(target.Wid))
        {
            if (_mobWid != target.Wid)
                (_mobWid, _mobSince) = (target.Wid, c.Now);
            if (c.Now - _mobSince > MobLimit)
            {
                c.Log.Info($"{target.Name}: не убить за {MobLimit.TotalSeconds:0} с — бросаю");
                _skip.Add(target.Wid);
                return c.Submit(new UnselectAction());
            }

            if (target.Offset.Horizontal > CastRange)
                return Walk(c, Toward(w.Host.Position, target.Position, CastRange - 3));

            ForgetWalk(c);
            var skill = c.Settings.Combat.AttackSkillId;
            if (w.Skill(skill) is not { IsReady: true })
                return true;
            c.Submit(new SkillAction(skill, target.Wid, approach: false, $"атака скиллом {skill}"));
            return true;
        }

        var mob = w.Mobs
            .Where(m => !m.IsDead && tids.Contains((uint)m.Tid) && !_skip.Contains(m.Wid) && m.Offset.Horizontal <= SeeRange)
            .OrderBy(m => m.Offset.Horizontal)
            .FirstOrDefault();
        if (mob is not null)
        {
            ForgetWalk(c);
            return c.Submit(new SelectTargetAction(mob));
        }

        // Рядом нет — к ближайшему ещё не осмотренному месту, где водятся
        var here = w.Host.Position;
        var spots = tids.SelectMany(t => _guide.Points.Of((int)t)).ToList();
        if (spots.Count == 0)
            return c.RequestStop($"Не знаю, где водятся {string.Join(", ", tids.Select(t => MobName(w, t)))} — останавливаюсь");

        foreach (var spot in spots.Where(s => s.HorizontalDistanceTo(here) <= VisitedRadius && !_visited.Contains(s)))
            _visited.Add(spot);
        var fresh = spots.Where(s => !_visited.Contains(s)).ToList();
        if (fresh.Count == 0)
        {
            _visited.Clear();
            fresh = spots;
        }

        Status += " — ищу";
        return Walk(c, fresh.OrderBy(s => s.HorizontalDistanceTo(here)).First());
    }

    // Первое задание «Доступных», которое игра разрешает взять сейчас (её же проверка, что у окна), не из «не брать» и не отказанное.
    // Проверка не вышла (нет в профиле) — по сохранённому списку игры (он бывает устаревшим: выполненное ещё там)
    private QuestInfo? NextOffer(BrainContext c)
    {
        var w = c.World;
        IEnumerable<QuestInfo> takeable = w.AvailableQuests ?? [];
        if (w.QuestCandidates is { } candidates)
        {
            var check = c.Executor.Control.Calls.QuestChecks(candidates.Select(q => q.Id).ToList());
            if (check is { Ok: true, Values: { } codes } && codes.Count == candidates.Count)
                takeable = candidates.Where((_, i) => codes[i] == 0);
            else
                c.Say("quest-check", $"Проверка «можно взять» не вышла ({check.Details}) — беру из списка игры", LogLevel.Warning, 60);
        }

        foreach (var quest in takeable.Where(q => q.GiverNpc != 0 && !_refused.Contains(q.Id) && !c.Settings.Quests.Skips(q.Name)))
        {
            if (quest.Options.Count == 0)
                return quest;

            // «Выполнить одно из подзаданий»: NPC выдаёт подзадание — первое, которое игра разрешает (как пункты его окна)
            var check = c.Executor.Control.Calls.QuestChecks(quest.Options.Select(o => o.Id).ToList());
            var option = check is { Ok: true, Values: { } codes } && codes.Count == quest.Options.Count
                ? quest.Options.Where((_, i) => codes[i] == 0).FirstOrDefault()
                : quest.Options[0];
            if (option is null)
            {
                _refused.Add(quest.Id);
                continue;
            }

            c.Log.Info($"«{quest.Name}» выдаётся подзаданием — беру «{option.Name}»");
            return option with { GiverNpc = option.GiverNpc != 0 ? option.GiverNpc : quest.GiverNpc };
        }

        return null;
    }

    // Взятое задание не попало в журнал, а игра больше не даёт его взять — выполнилось сразу при взятии (тип 5)
    private static bool DoneOnTake(BrainContext c, int task)
        => c.Executor.Control.Calls.QuestChecks([task]) is { Ok: true, Values: { Count: 1 } codes } && codes[0] != 0;

    // Подойти к NPC и заговорить; потом (Dialog) — взять или сдать задание и закрыть окно
    private bool Visit(BrainContext c, uint npcTid, int task, bool accept, string what)
    {
        var w = c.World;
        var npc = w.Npcs.Where(n => n.Kind == NpcKind.Npc && n.Tid == npcTid).OrderBy(n => n.Offset.Horizontal).FirstOrDefault();
        var name = npc?.Name ?? $"NPC {npcTid}";
        Status = $"задания: {what} — {name}";

        if (npc is { Offset.Horizontal: <= TalkDistance })
        {
            if (c.Runner.BodyBusy(w) is not null)
                return true;
            ForgetWalk(c);
            if (c.Send(new NpcHelloAction(npc.Wid)) != SubmitStatus.Sent)
                return true;
            (_stage, _stageAt, _dealTask, _dealAccept) = (Stage.Hello, c.Now, task, accept);
            c.Log.Info($"Задания: {what} — говорю с {name}");
            return true;
        }

        var point = npc is not null ? Toward(w.Host.Position, npc.Position, NpcStop) : _guide.Points.Nearest((int)npcTid, w.Host.Position);
        if (point is null)
            return c.RequestStop($"Не знаю, где {name} ({what}) — останавливаюсь");
        return Walk(c, point.Value);
    }

    // Разговор начат: подождать окно → взять или сдать → дождаться списка → закрыть окно
    private bool Dialog(BrainContext c, IReadOnlyList<QuestInfo> quests)
    {
        if (_stage == Stage.After)
            return After(c);

        if (_stage == Stage.Hello)
        {
            Status = "задания: говорю с NPC";
            if (c.Now - _stageAt < DialogWait)
                return true;
            c.Send(_dealAccept ? new NpcAcceptTaskAction(_dealTask) : new NpcReturnTaskAction(_dealTask));
            (_stage, _stageAt) = (Stage.Deal, c.Now);
            return true;
        }

        var has = quests.Any(q => q.Id == _dealTask);
        var instant = _dealAccept && !has && c.Now - _stageAt >= DialogWait && DoneOnTake(c, _dealTask);
        var done = _dealAccept ? has || instant : !has;
        Status = _dealAccept ? $"задания: беру {_dealTask}" : $"задания: сдаю {_dealTask}";
        if (!done && c.Now - _stageAt < DealWait)
            return true;

        if (done)
        {
            c.Log.Info(instant ? $"Задание {_dealTask} выполнено сразу при взятии"
                : _dealAccept ? $"Взял задание {Title(quests, _dealTask)}" : $"Сдал задание {_dealTask}");
            _tries = 0;
            _offer = null;
            if (!_dealAccept && _guide.Scripts?.TryGetValue(_dealTask, out var script) == true && script.After.Count > 0)
            {
                // Окно не закрываем: шаги — у того же NPC
                var problems = new List<string>();
                _steps.Clear();
                foreach (var step in QuestStepKinds.ParseAll(script.After, problems))
                    _steps.Enqueue(step);
                foreach (var problem in problems)
                    c.Log.Warning($"Скрипт задания {_dealTask}: {problem} — пропускаю");
                (_stage, _stageAt) = (Stage.After, c.Now);
                return true;
            }
        }
        else if (++_tries >= DealTries)
        {
            c.Send(new NpcDialogCloseAction());
            _stage = Stage.Idle;
            _tries = 0;
            if (_dealAccept)
            {
                c.Log.Warning($"NPC не даёт задание {_dealTask} ({DealTries} раза) — пропускаю его");
                _refused.Add(_dealTask);
                _offer = null;
                return true;
            }
            return c.RequestStop($"NPC не {(_dealAccept ? "даёт" : "принимает")} задание {_dealTask} ({DealTries} раза) — останавливаюсь");
        }
        else
        {
            c.Log.Warning($"Задание {_dealTask}: NPC не {(_dealAccept ? "дал" : "принял")} — попробую ещё раз");
        }

        c.Send(new NpcDialogCloseAction());
        _stage = Stage.Idle;
        return true;
    }

    // После сдачи, в том же разговоре: шаги скрипта по порядку; кончились — закрыть окно
    private bool After(BrainContext c)
    {
        Status = $"задания: после сдачи {_dealTask}{(_steps.Count > 0 ? $" — {_steps.Peek().Title}" : "")}";
        if (c.Now - _stageAt < RewardWait)
            return true;

        while (_steps.Count > 0)
        {
            if (_steps.Peek().Tick(c, _dealTask) == QuestStepState.Running)
                return true;
            _steps.Dequeue();
        }

        c.Send(new NpcDialogCloseAction());
        _stage = Stage.Idle;
        return true;
    }

    // Идти «умно»; уже идём туда же — ждём, в другое место — заменить
    private static bool Walk(BrainContext c, Position point)
    {
        if (c.Mine.OfType<MoveAction>().FirstOrDefault() is { } walking)
        {
            if (walking.Point.HorizontalDistanceTo(point) <= 3)
                return true;
            return c.Replace(new MoveAction(point, tolerance: 2f, smart: true)) is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
        }

        return c.Submit(new MoveAction(point, tolerance: 2f, smart: true));
    }

    private static void ForgetWalk(BrainContext c)
    {
        foreach (var walking in c.Mine.OfType<MoveAction>().ToList())
            c.Forget(walking);
    }

    // Точка на линии от перса к цели, не доходя до неё stop метров; высота — цели (земля у неё)
    private static Position Toward(Position from, Position to, float stop)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = (float)Math.Sqrt(dx * dx + dy * dy);
        var keep = length <= stop ? 0f : (length - stop) / length;
        return new Position(from.X + dx * keep, to.Height, from.Y + dy * keep);
    }

    // Цель по мобу выполнена: с него падает предмет — хватает ли их в сумке заданий; иначе — убито ли сколько нужно
    private static bool Enough(WorldState w, QuestTarget mob)
        => mob.DropItem != 0 ? w.QuestItemCount(mob.DropItem) >= mob.DropNeed : (mob.Done ?? 0) >= mob.Need;

    private static string Progress(WorldState w, QuestTarget mob)
        => mob.DropItem != 0 ? $"предмет {w.QuestItemCount(mob.DropItem)}/{mob.DropNeed}" : $"{mob.Done ?? 0}/{mob.Need}";

    private static string MobName(WorldState w, uint tid) =>
        w.Npcs.FirstOrDefault(n => n.Tid == tid)?.Name ?? $"моб {tid}";

    private static string Title(IReadOnlyList<QuestInfo> quests, int id) =>
        quests.FirstOrDefault(q => q.Id == id) is { } q ? $"«{q.Name}»" : id.ToString();

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is SkillAction && outcome.Status == ActionStatus.Rejected)
            c.Say("quest-skill", $"Скилл не прошёл: {outcome.Details}", LogLevel.Debug, 10);
    }

    public void Reset()
    {
        _stage = Stage.Idle;
        _tries = 0;
        _steps.Clear();
        _offer = null;
        _refused.Clear();
        _mobWid = 0;
        _skip.Clear();
        _visited.Clear();
        Status = null;
    }
}

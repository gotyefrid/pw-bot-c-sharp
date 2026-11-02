using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.GameFiles;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>Что нужно заданиям, кроме снимка: где водятся мобы и стоят NPC, с какого задания начать.</summary>
/// <param name="Points">Опорные точки по tid (configs\coord_data.txt).</param>
/// <param name="StartTask">Первое задание, если принятых нет; 0 — не задано (тогда без заданий бот просто встанет).</param>
/// <param name="StartNpc">tid NPC, который даёт первое задание.</param>
public sealed record QuestGuide(GuidePoints Points, int StartTask, uint StartNpc);

/// <summary>
/// «Прокачать друида»: делает принятые задания по журналу. Убить — бьёт мобов цели скиллом издалека (без подхода «как кнопкой»):
/// видит такого — выбирает и бьёт, далеко — подходит «умно» на дальность скилла, нет рядом — идёт к местам, где они водятся. Цель
/// выполнена или задание «поговорить» — идёт «умно» к NPC, заговаривает, сдаёт и закрывает окно. Следующее задание игра выдаёт
/// сама; принятых нет — берёт первое (<see cref="QuestGuide.StartTask"/>), уже брал — встаёт: задания закончились.
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
    private const int DealTries = 3;

    private enum Stage { Idle, Hello, Deal }

    private readonly QuestGuide _guide;
    private readonly HashSet<uint> _skip = [];
    private readonly List<Position> _visited = [];

    private Stage _stage;
    private DateTime _stageAt;
    private int _dealTask;
    private bool _dealAccept;
    private int _tries;
    private bool _started;
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
            if (!_started && _guide.StartTask != 0 && _guide.StartNpc != 0)
                return Visit(c, _guide.StartNpc, _guide.StartTask, accept: true, "взять первое задание");
            return c.RequestStop("Задания закончились");
        }

        _started = true;
        switch (quest.Goal)
        {
            case QuestGoals.Kill:
                var left = quest.Monsters.Where(m => (m.Done ?? 0) < m.Need).ToList();
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
        Status = $"задание «{quest.Name}»: {string.Join(", ", left.Select(t => $"{MobName(w, t.Tid)} {t.Done ?? 0}/{t.Need}"))}";

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
        var done = _dealAccept ? has : !has;
        Status = _dealAccept ? $"задания: беру {_dealTask}" : $"задания: сдаю {_dealTask}";
        if (!done && c.Now - _stageAt < DealWait)
            return true;

        if (done)
        {
            c.Log.Info(_dealAccept ? $"Взял задание {Title(quests, _dealTask)}" : $"Сдал задание {_dealTask}");
            _tries = 0;
            _started |= _dealAccept;
        }
        else if (++_tries >= DealTries)
        {
            c.Send(new NpcDialogCloseAction());
            _stage = Stage.Idle;
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
        _started = false;
        _mobWid = 0;
        _skip.Clear();
        _visited.Clear();
        Status = null;
    }
}

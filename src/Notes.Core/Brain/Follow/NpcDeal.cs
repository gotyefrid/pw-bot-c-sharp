using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

public enum DealStep
{
    Going,
    Done,
    Failed,
}

/// <summary>
/// Одна услуга NPC: подойти по прямой (загораживает препятствие — своим обходом, <see cref="ObstacleWalk"/>; не автопутём клиента)
/// на <see cref="TalkDistance"/>, «привет», через <see cref="DialogWait"/> — попросить (взять или сдать задание, выучить скилл),
/// дождаться итога в снимке (<see cref="DealWait"/>), закрыть окно. Не вышло — ещё раз, всего <see cref="Tries"/> раза. Тот же
/// разговор, что у «Прокачать друида» (<see cref="QuestBehavior"/>), без его скриптов после сдачи.
/// </summary>
public sealed class NpcDeal
{
    public const int Tries = 3;
    private const float TalkDistance = 4f;
    private const float NpcStop = 2.5f;
    private static readonly TimeSpan DialogWait = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan DealWait = TimeSpan.FromSeconds(4);

    private enum Stage { Walk, Hello, Deal }

    private readonly int _npcTid;
    private readonly Func<GameAction> _request;
    private readonly Func<WorldState, bool> _done;
    private readonly string _doneLog;
    private readonly string _refused;
    private Stage _stage;
    private DateTime _at;
    private int _tries;
    private readonly ObstacleWalk _walk = new();

    /// <param name="npcTid">К какому NPC (tid).</param>
    /// <param name="key">Что делаем — номер задания или скилла (для списка «не вышло» у вызывающего).</param>
    /// <param name="what">Для лога и окна: «взять «Триумф»».</param>
    /// <param name="request">Просьба NPC (новое действие на каждую попытку).</param>
    /// <param name="done">Итог в снимке: вышло.</param>
    /// <param name="doneLog">В лог, когда вышло.</param>
    /// <param name="refused">Чего NPC не сделал — «дал задание 1179».</param>
    public NpcDeal(int npcTid, int key, string what, Func<GameAction> request, Func<WorldState, bool> done, string doneLog, string refused)
    {
        _npcTid = npcTid;
        Key = key;
        What = what;
        _request = request;
        _done = done;
        _doneLog = doneLog;
        _refused = refused;
    }

    /// <summary>Взять (<paramref name="accept"/>) или сдать задание; <paramref name="choice"/> — награда на выбор с 0.</summary>
    public static NpcDeal Task(int npcTid, int task, bool accept, string what, int choice = 0)
        => new(npcTid, task, what,
            () => accept ? new NpcAcceptTaskAction(task) : new NpcReturnTaskAction(task, choice),
            w => w.Quests is { } journal && journal.Any(q => q.Id == task) == accept,
            accept ? $"Взял задание {task}" : $"Сдал задание {task}",
            accept ? $"дал задание {task}" : $"принял задание {task}");

    /// <summary>Выучить скилл или поднять его уровень на один: было <paramref name="levelBefore"/> (0 — скилла нет).</summary>
    public static NpcDeal LearnSkill(int npcTid, int skill, int levelBefore, string what)
        => new(npcTid, skill, what,
            () => new NpcLearnSkillAction(skill),
            w => (w.Skill(skill)?.Level ?? 0) > levelBefore,
            levelBefore == 0 ? $"Выучил скилл {skill}" : $"Поднял скилл {skill} до ур. {levelBefore + 1}",
            $"учит скилл {skill}");

    public int Key { get; }
    public string What { get; }

    /// <summary>Почему не вышло (<see cref="DealStep.Failed"/>).</summary>
    public string? Failed { get; private set; }

    public DealStep Tick(BrainContext c)
    {
        var w = c.World;
        switch (_stage)
        {
            case Stage.Hello:
                if (c.Now - _at < DialogWait)
                    return DealStep.Going;
                c.Send(_request());
                (_stage, _at) = (Stage.Deal, c.Now);
                return DealStep.Going;

            case Stage.Deal:
                if (_done(w))
                {
                    c.Log.Info(_doneLog);
                    c.Send(new NpcDialogCloseAction());
                    return DealStep.Done;
                }
                if (c.Now - _at < DealWait)
                    return DealStep.Going;
                c.Send(new NpcDialogCloseAction());
                if (++_tries >= Tries)
                    return Fail($"NPC не {_refused} ({Tries} раза)");
                c.Log.Warning($"{What}: NPC не {_refused} — попробую ещё раз");
                _stage = Stage.Walk;
                return DealStep.Going;
        }

        var npc = w.Npcs.Where(n => n.Kind == NpcKind.Npc && n.Tid == _npcTid).OrderBy(n => n.Offset.Direct).FirstOrDefault();
        if (npc is null)
            return Fail($"NPC {_npcTid} пропал из виду");

        // Говорить — по прямой, с высотой: NPC на этаж выше или ниже сервер не слышит
        if (npc.Offset.Direct <= TalkDistance)
        {
            _walk.Stop(c);
            if (c.Runner.BodyBusy(w) is not null)
                return DealStep.Going;
            foreach (var walking in c.Mine.OfType<MoveAction>().ToList())
                c.Forget(walking);
            if (c.Send(new NpcHelloAction(npc.Wid)) == SubmitStatus.Sent)
                (_stage, _at) = (Stage.Hello, c.Now);
            return DealStep.Going;
        }

        if (npc.Offset.WithinOnFoot(TalkDistance, NpcStop))
            return Fail($"{npc.Name} {(npc.Offset.Vertical > 0 ? "выше" : "ниже")} на {Math.Abs(npc.Offset.Vertical):0} м — не подойти");

        var point = Toward(w.Host.Position, npc.Position, Math.Max(1f, npc.Offset.FlatWithin(NpcStop)));
        if (_walk.Step(c, point, npc.Name))
            return DealStep.Going;
        if (c.Mine.OfType<MoveAction>().FirstOrDefault() is { } going && going.Point.HorizontalDistanceTo(point) <= 3)
            return DealStep.Going;
        var sent = c.Mine.OfType<MoveAction>().Any() ? c.Replace(new MoveAction(point, 2f)) : c.Send(new MoveAction(point, 2f));
        return sent == SubmitStatus.Failed ? Fail("не идёт к NPC") : DealStep.Going;
    }

    private DealStep Fail(string why)
    {
        Failed = why;
        return DealStep.Failed;
    }

    // Точка на линии от перса к цели, не доходя до неё stop метров; высота — цели
    private static Position Toward(Position from, Position to, float stop)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var length = (float)Math.Sqrt(dx * dx + dy * dy);
        var keep = length <= stop ? 0f : (length - stop) / length;
        return new Position(from.X + dx * keep, to.Height, from.Y + dy * keep);
    }
}

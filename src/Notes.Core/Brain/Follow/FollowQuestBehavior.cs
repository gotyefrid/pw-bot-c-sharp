using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// «Следовать», задания: повторяет за главным взятие и сдачу по его журналу (журнал главного — с табло, <see cref="LeaderBoard"/>).
/// Взять — в журнале главного задание, которого нет у нас, игра разрешает его взять (та же проверка, что у окна «Доступные»), и
/// NPC, который его даёт, виден рядом (<see cref="NpcRange"/>): главный только что взял его у этого NPC, мы идём следом. Задания,
/// которые выдаёт сама игра (следующее подзадание), не берём — придут сами. Сдать — задание было в журнале главного, а теперь
/// его нет (сдал), у нас оно выполнено, и NPC, который принимает, виден рядом. Не выполнено — добиваем мобов цели, которых видно
/// рядом (просьба бою, <see cref="FightState.Prey"/>), сами за ними далеко не ходим. Награда на выбор — та же, что взял главный
/// (табло узнаёт её по его сумке, <see cref="LeaderBoard.RewardChoice"/>); не узнали за <see cref="LeaderBoard.RewardWait"/> —
/// не гадаем: задание не сдаём, пишем в лог, сдать — руками. NPC не дал или не принял <see cref="NpcTaskDeal.Tries"/>
/// раза — задание пропускаем до перезапуска, бота не останавливаем.
/// </summary>
public sealed class FollowQuestBehavior(LeaderBoard board) : IBehavior
{
    /// <summary>NPC дальше этого не идём: главный брал или сдавал рядом с нами.</summary>
    public const float NpcRange = 60f;

    // Мобов цели своего задания ищем не дальше
    private const float HuntRange = 40f;

    // Задания, которые были в журнале главного в этот запуск
    private readonly HashSet<int> _leaderHad = [];
    // Не берём и не сдаём больше в этот запуск: игра не даёт, NPC отказал, не знаем, кто даёт
    private readonly HashSet<int> _skip = [];
    // Когда заметили, что главный сдал задание
    private readonly Dictionary<int, DateTime> _leaderHanded = [];
    // Choice: ждём, какую награду выбрал главный
    private bool _waitingReward;
    private NpcTaskDeal? _deal;
    // Кого просили убить ради своего задания
    private uint _hunted;

    public string Name => "задания за главным";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var busy = Step(c);
        if (_hunted != 0 && c.Fight.Prey != _hunted)
            _hunted = 0;
        return busy;
    }

    private bool Step(BrainContext c)
    {
        if (!c.Settings.Follow.Quests || Leader.Of(c, board) is not { } leader)
        {
            _deal = null;
            return false;
        }

        c.Executor.Control.Requests.Quests = true;
        board.WantQuests(leader.Host.Name, c.Now);
        if (c.World.Quests is not { } mine || leader.Quests is not { } theirs)
        {
            Status = "задания: читаю журналы";
            return false;
        }

        if (c.Runner.IsPending(ActionSlot.Bank))
        {
            Status = "задания: жду ответа NPC";
            return true;
        }

        foreach (var quest in theirs)
            _leaderHad.Add(quest.Id);

        if (_deal is { } deal)
            return Continue(c, deal, mine);

        // Главный сдал — сдаём и мы (выполненное), не выполненное — добиваем мобов рядом
        foreach (var quest in mine.Where(q => _leaderHad.Contains(q.Id) && theirs.All(t => t.Id != q.Id) && !_skip.Contains(q.Id)))
        {
            if (quest.AwardNpc == 0)
                continue;
            if (!_leaderHanded.ContainsKey(quest.Id))
                _leaderHanded[quest.Id] = c.Now;
            if (Done(c.World, quest))
            {
                // Ждём, какую награду выбрал главный, — стоим у NPC
                if (Choice(c, leader.Host.Name, quest) is not { } choice)
                {
                    if (_waitingReward)
                        return true;
                    continue;
                }
                if (Npc(c.World, quest.AwardNpc) is { } npc)
                    return Begin(c, new NpcTaskDeal(npc.Tid, quest.Id, accept: false,
                        choice > 0 || quest.RewardChoices is { Count: > 1 } ? $"сдать «{quest.Name}», награда {choice + 1}" : $"сдать «{quest.Name}»", choice), mine);
                Status = $"задания: сдать «{quest.Name}» — не вижу NPC рядом";
                continue;
            }
            if (Hunt(c, quest))
                return false;
        }

        // Главный взял — берём и мы
        foreach (var quest in theirs.Where(t => mine.All(q => q.Id != t.Id) && !_skip.Contains(t.Id)))
        {
            var giver = Giver(c.World, quest.Id);
            if (giver == 0)
            {
                c.Log.Debug($"Задания: «{quest.Name}» у главного — его выдаёт игра или не знаю кто; не беру сам");
                _skip.Add(quest.Id);
                continue;
            }

            if (c.Executor.Control.Calls.QuestChecks([quest.Id]) is { Ok: true, Values: { Count: 1 } codes } && codes[0] != 0)
            {
                c.Log.Info($"Задания: «{quest.Name}» у главного, а мне игра его не даёт (код {codes[0]}) — пропускаю");
                _skip.Add(quest.Id);
                continue;
            }

            if (Npc(c.World, giver) is { } npc)
                return Begin(c, new NpcTaskDeal(npc.Tid, quest.Id, accept: true, $"взять «{quest.Name}»"), mine);
            Status = $"задания: взять «{quest.Name}» — не вижу NPC рядом";
        }

        return false;
    }

    private bool Begin(BrainContext c, NpcTaskDeal deal, IReadOnlyList<QuestInfo> mine)
    {
        c.Log.Info($"Задания за главным: {deal.What}");
        _deal = deal;
        return Continue(c, deal, mine);
    }

    private bool Continue(BrainContext c, NpcTaskDeal deal, IReadOnlyList<QuestInfo> mine)
    {
        var step = deal.Tick(c, mine);
        Status = $"задания: {deal.What}";
        switch (step)
        {
            case DealStep.Going:
                return true;
            case DealStep.Done:
                _deal = null;
                return true;
            default:
                c.Log.Warning($"Задания за главным: {deal.What} — не вышло ({deal.Failed}), пропускаю");
                _skip.Add(deal.Task);
                _deal = null;
                return true;
        }
    }

    // Своё задание «убить» не выполнено, а главный уже сдал: моб цели рядом — просим бой. true — попросили
    private bool Hunt(BrainContext c, QuestInfo quest)
    {
        if (quest.Goal != QuestGoals.Kill || c.Fight.Prey != 0 && c.Fight.Prey != _hunted)
            return false;

        var tids = new HashSet<int>(quest.Monsters.Where(m => !QuestBehavior.Enough(c.World, m)).Select(m => (int)m.Tid));
        var mob = c.World.Mobs.Where(m => !m.IsDead && tids.Contains(m.Tid) && m.Offset.Direct <= HuntRange).OrderBy(m => m.Offset.Direct).FirstOrDefault();
        if (mob is null)
        {
            Status = $"задания: «{quest.Name}» у главного сдано, у меня не выполнено — мобов цели рядом нет";
            c.Say($"follow-quest-{quest.Id}", $"Задания: «{quest.Name}» главный сдал, а у меня не выполнено — бью мобов цели, когда они рядом", seconds: 120);
            return false;
        }

        if (_hunted != mob.Wid)
            c.Log.Info($"Задания: «{quest.Name}» — добиваю {mob.Name}");
        _hunted = mob.Wid;
        c.Fight.Prey = mob.Wid;
        Status = $"задания: «{quest.Name}» — {mob.Name}";
        return true;
    }

    // Какую награду брать: выбора нет — 0; есть — ту же, что главный. null — пока не знаем (ждём) или не узнали (пропускаем)
    private int? Choice(BrainContext c, string leader, QuestInfo quest)
    {
        _waitingReward = false;
        if (quest.RewardChoices is not { Count: > 1 })
            return 0;
        if (board.RewardChoice(leader, quest.Id) is { } choice)
            return choice < quest.RewardChoices.Count ? choice : null;

        if (c.Now - _leaderHanded[quest.Id] <= LeaderBoard.RewardWait + TimeSpan.FromSeconds(2))
        {
            Status = $"задания: «{quest.Name}» — жду, какую награду выбрал главный";
            _waitingReward = true;
            return null;
        }

        c.Log.Warning($"Задания: «{quest.Name}» — не узнал, какую награду выбрал главный (сумка полна или награда не пришла); "
                      + "не сдаю — сдайте руками");
        _skip.Add(quest.Id);
        return null;
    }

    private static bool Done(WorldState w, QuestInfo quest) => quest.Goal switch
    {
        QuestGoals.Kill => quest.Monsters.All(m => QuestBehavior.Enough(w, m)),
        QuestGoals.Talk => true,
        _ => quest.Monsters.All(m => QuestBehavior.Enough(w, m)),
    };

    private static NpcInfo? Npc(WorldState w, uint tid)
        => w.Npcs.Where(n => n.Kind == NpcKind.Npc && n.Tid == tid && n.Offset.Direct <= NpcRange).OrderBy(n => n.Offset.Direct).FirstOrDefault();

    // Кто даёт задание: из «Доступных» (и всех, что игра может дать); подзадание «одно из» — его NPC или NPC родителя. 0 — не знаем
    private static uint Giver(WorldState w, int task)
    {
        foreach (var quest in (w.QuestCandidates ?? []).Concat(w.AvailableQuests ?? []))
        {
            if (quest.Id == task && quest.GiverNpc != 0)
                return quest.GiverNpc;
            if (quest.Options.FirstOrDefault(o => o.Id == task) is { } option)
                return option.GiverNpc != 0 ? option.GiverNpc : quest.GiverNpc;
        }

        return 0;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _leaderHad.Clear();
        _skip.Clear();
        _leaderHanded.Clear();
        _deal = null;
        _hunted = 0;
        Status = null;
    }
}

public enum DealStep
{
    Going,
    Done,
    Failed,
}

/// <summary>
/// Взять или сдать одно задание у NPC: подойти по прямой (загораживает препятствие — своим обходом, <see cref="ObstacleWalk"/>; не
/// автопутём клиента) на <see cref="TalkDistance"/>, «привет», через <see cref="DialogWait"/> — взять
/// или сдать, дождаться журнала (<see cref="DealWait"/>), закрыть окно. Не вышло — ещё раз, всего <see cref="Tries"/> раза. Тот же
/// разговор, что у «Прокачать друида» (<see cref="QuestBehavior"/>), без его скриптов после сдачи.
/// </summary>
public sealed class NpcTaskDeal(int npcTid, int task, bool accept, string what, int choice = 0)
{
    public const int Tries = 3;
    private const float TalkDistance = 4f;
    private const float NpcStop = 2.5f;
    private static readonly TimeSpan DialogWait = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DealWait = TimeSpan.FromSeconds(4);

    private enum Stage { Walk, Hello, Deal }

    private Stage _stage;
    private DateTime _at;
    private int _tries;
    private readonly ObstacleWalk _walk = new();

    public int Task { get; } = task;
    public string What { get; } = what;

    /// <summary>Почему не вышло (<see cref="DealStep.Failed"/>).</summary>
    public string? Failed { get; private set; }

    public DealStep Tick(BrainContext c, IReadOnlyList<QuestInfo> journal)
    {
        var w = c.World;
        switch (_stage)
        {
            case Stage.Hello:
                if (c.Now - _at < DialogWait)
                    return DealStep.Going;
                c.Send(accept ? new NpcAcceptTaskAction(Task) : new NpcReturnTaskAction(Task, choice));
                (_stage, _at) = (Stage.Deal, c.Now);
                return DealStep.Going;

            case Stage.Deal:
                var has = journal.Any(q => q.Id == Task);
                if (accept == has)
                {
                    c.Log.Info(accept ? $"Взял задание {Task}" : $"Сдал задание {Task}");
                    c.Send(new NpcDialogCloseAction());
                    return DealStep.Done;
                }
                if (c.Now - _at < DealWait)
                    return DealStep.Going;
                c.Send(new NpcDialogCloseAction());
                if (++_tries >= Tries)
                {
                    Failed = $"NPC не {(accept ? "дал" : "принял")} задание {Task} ({Tries} раза)";
                    return DealStep.Failed;
                }
                c.Log.Warning($"Задание {Task}: NPC не {(accept ? "дал" : "принял")} — попробую ещё раз");
                _stage = Stage.Walk;
                return DealStep.Going;
        }

        var npc = w.Npcs.Where(n => n.Kind == NpcKind.Npc && n.Tid == npcTid).OrderBy(n => n.Offset.Direct).FirstOrDefault();
        if (npc is null)
        {
            Failed = $"NPC {npcTid} пропал из виду";
            return DealStep.Failed;
        }

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
        {
            Failed = $"{npc.Name} {(npc.Offset.Vertical > 0 ? "выше" : "ниже")} на {Math.Abs(npc.Offset.Vertical):0} м — не подойти";
            return DealStep.Failed;
        }

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

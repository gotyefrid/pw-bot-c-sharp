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
/// которые выдаёт сама игра (следующее подзадание), не берём — придут сами; «выполнить одно из подзаданий» само не берём — берём
/// подзадание. Игра пока не даёт (не сдано предыдущее) — проверяем снова через <see cref="Recheck"/>. Сдать — у главного в журнале
/// нет ни этого задания, ни его цепочки (прошёл её; номера подзаданий у нас бывают разные), у нас оно выполнено, и NPC, который
/// принимает, виден рядом. Не выполнено — добиваем мобов цели, которых видно
/// рядом (просьба бою, <see cref="FightState.Prey"/>), сами за ними далеко не ходим. Награда на выбор — та же, что взял главный
/// (табло узнаёт её по его сумке, <see cref="LeaderBoard.RewardChoice"/>); не узнали за <see cref="LeaderBoard.RewardWait"/> —
/// не гадаем: задание не сдаём, пишем в лог, сдать — руками. NPC не дал или не принял <see cref="NpcDeal.Tries"/>
/// раза — задание пропускаем на <see cref="DealRetry"/>, бота не останавливаем.
/// </summary>
public sealed class FollowQuestBehavior(LeaderBoard board) : IBehavior
{
    /// <summary>NPC дальше этого не идём: главный брал или сдавал рядом с нами.</summary>
    public const float NpcRange = 60f;

    // Мобов цели своего задания ищем не дальше
    private const float HuntRange = 40f;

    // Не берём и не сдаём до этого времени: игра пока не даёт (не сдано предыдущее), NPC отказал, не знаем, кто даёт
    private readonly Dictionary<int, DateTime> _skip = [];
    // Когда заметили, что главный сдал задание
    private readonly Dictionary<int, DateTime> _leaderHanded = [];
    // Choice: ждём, какую награду выбрал главный
    private bool _waitingReward;
    private NpcDeal? _deal;
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

        if (_deal is { } deal)
            return Continue(c, deal);

        // Главный прошёл — сдаём и мы (выполненное), не выполненное — добиваем мобов рядом. «Прошёл» — у него в журнале нет ни этого
        // задания, ни его цепочки: номера подзаданий у главного и у нас бывают разные, а сдать он мог быстрее, чем мы увидели
        foreach (var quest in mine.Where(q => Passed(q, mine, theirs) && !Skipped(c, q.Id)))
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
                    return Begin(c, NpcDeal.Task(npc.Tid, quest.Id, accept: false,
                        choice > 0 || quest.RewardChoices is { Count: > 1 } ? $"сдать «{quest.Name}», награда {choice + 1}" : $"сдать «{quest.Name}»", choice));
                Status = $"задания: сдать «{quest.Name}» — не вижу NPC рядом";
                continue;
            }
            if (Hunt(c, quest))
                return false;
        }

        // Главный взял — берём и мы
        foreach (var quest in theirs.Where(t => mine.All(q => q.Id != t.Id) && !Skipped(c, t.Id)))
        {
            // «Выполнить одно из подзаданий»: само не берётся — берём подзадание, как главный (оно тоже в его журнале)
            if (ChooseOne(c.World, quest.Id))
                continue;

            var giver = Giver(c.World, quest.Id);
            if (giver == 0)
            {
                c.Log.Debug($"Задания: «{quest.Name}» у главного — его выдаёт игра или не знаю кто; не беру сам");
                Skip(c, quest.Id, Recheck);
                continue;
            }

            if (c.Executor.Control.Calls.QuestChecks([quest.Id]) is { Ok: true, Values: { Count: 1 } codes } && codes[0] != 0)
            {
                c.Say($"follow-take-{quest.Id}", $"Задания: «{quest.Name}» у главного, а мне игра его пока не даёт (код {codes[0]}) — "
                                                 + "проверю позже (не сдано предыдущее?)", seconds: 300);
                Skip(c, quest.Id, Recheck);
                continue;
            }

            if (Npc(c.World, giver) is { } npc)
                return Begin(c, NpcDeal.Task(npc.Tid, quest.Id, accept: true, $"взять «{quest.Name}»"));
            Status = $"задания: взять «{quest.Name}» — не вижу NPC рядом";
        }

        return false;
    }

    private bool Begin(BrainContext c, NpcDeal deal)
    {
        c.Log.Info($"Задания за главным: {deal.What}");
        _deal = deal;
        return Continue(c, deal);
    }

    private bool Continue(BrainContext c, NpcDeal deal)
    {
        var step = deal.Tick(c);
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
                Skip(c, deal.Key, DealRetry);
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
        Skip(c, quest.Id, TimeSpan.FromDays(1));
        return null;
    }

    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DealRetry = TimeSpan.FromSeconds(60);

    private bool Skipped(BrainContext c, int task) => _skip.TryGetValue(task, out var until) && c.Now < until;

    private void Skip(BrainContext c, int task, TimeSpan time) => _skip[task] = c.Now + time;

    // У главного нет ни задания, ни его цепочки (верхнего задания в нашем журнале)
    private static bool Passed(QuestInfo quest, IReadOnlyList<QuestInfo> mine, IReadOnlyList<QuestInfo> theirs)
    {
        var root = quest;
        for (var depth = 0; root.ParentId != 0 && depth < 8; depth++)
            root = mine.FirstOrDefault(q => q.Id == root.ParentId) ?? root;
        return theirs.All(t => t.Id != quest.Id && t.Id != root.Id);
    }

    // Задание «выполнить одно из подзаданий» (по «Доступным»): само не берётся
    private static bool ChooseOne(WorldState w, int task)
        => (w.QuestCandidates ?? []).Concat(w.AvailableQuests ?? []).Any(q => q.Id == task && q.Options.Count > 0);

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
        _skip.Clear();
        _leaderHanded.Clear();
        _deal = null;
        _hunted = 0;
        Status = null;
    }
}

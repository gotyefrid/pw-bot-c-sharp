using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// «Следовать», скиллы: у главного скилл, которого у нас нет, или уровень выше — идём к NPC, у которого он учился (табло:
/// <see cref="LeaderBoard.Teacher"/>), и учим, по уровню за разговор, пока не сравняемся. Учитель должен быть виден рядом
/// (<see cref="FollowQuestBehavior.NpcRange"/>): главный только что учился у него, мы идём следом. Учителя не знаем (скилл дала
/// игра) — не учим. NPC не научил <see cref="NpcDeal.Tries"/> раза (денег, очков, уровня или книги не хватает) — пишем в лог и
/// не пробуем, пока главный не поднимет этот скилл выше.
/// </summary>
public sealed class FollowSkillsBehavior(LeaderBoard board) : IBehavior
{
    // Скилл → уровень главного, до которого научиться не вышло
    private readonly Dictionary<int, int> _failed = [];
    private readonly HashSet<int> _noTeacher = [];
    private NpcDeal? _deal;
    private int _dealTarget;

    public string Name => "скиллы за главным";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!c.Settings.Follow.Skills || Leader.Of(c, board) is not { } leader)
        {
            _deal = null;
            return false;
        }

        if (c.Runner.IsPending(ActionSlot.Bank))
        {
            Status = "скиллы: жду ответа NPC";
            return true;
        }

        if (_deal is { } deal)
            return Continue(c, deal);

        foreach (var skill in leader.Skills)
        {
            var mine = c.World.Skill(skill.Id)?.Level ?? 0;
            if (skill.Level <= mine || _failed.TryGetValue(skill.Id, out var failedAt) && failedAt >= skill.Level)
                continue;

            var title = skill.Name is { Length: > 0 } name ? $"«{name}»" : $"{skill.Id}";
            if (board.Teacher(leader.Host.Name, skill.Id) is not { } teacher)
            {
                if (_noTeacher.Add(skill.Id))
                    c.Log.Debug($"Скиллы: {title} у главного ур. {skill.Level}, у меня {mine} — не знаю, у кого он учился; не учу");
                continue;
            }

            var npc = c.World.Npcs.Where(n => n.Kind == NpcKind.Npc && n.Tid == teacher && n.Offset.Direct <= FollowQuestBehavior.NpcRange)
                .OrderBy(n => n.Offset.Direct).FirstOrDefault();
            if (npc is null)
            {
                Status = $"скиллы: {title} — не вижу учителя рядом";
                continue;
            }

            var what = mine == 0 ? $"выучить {title}" : $"поднять {title} до ур. {mine + 1}";
            c.Log.Info($"Скиллы за главным: {what} у {npc.Name}");
            _deal = NpcDeal.LearnSkill(npc.Tid, skill.Id, mine, what);
            _dealTarget = skill.Level;
            return Continue(c, _deal);
        }

        return false;
    }

    private bool Continue(BrainContext c, NpcDeal deal)
    {
        var step = deal.Tick(c);
        Status = $"скиллы: {deal.What}";
        if (step == DealStep.Going)
            return true;
        if (step == DealStep.Failed)
        {
            c.Log.Warning($"Скиллы за главным: {deal.What} — не вышло ({deal.Failed}); денег, очков, уровня или книги не хватает? "
                          + "Больше не пробую, пока главный не поднимет его выше");
            _failed[deal.Key] = _dealTarget;
        }

        _deal = null;
        return true;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _failed.Clear();
        _noTeacher.Clear();
        _deal = null;
        Status = null;
    }
}

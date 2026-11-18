using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// «Следовать», ассист: главный бьёт моба — просит бой (<see cref="FightState.Prey"/>) убить того же (WID у мобов общий на всех
/// клиентах). Бьёт бой из «Общего» тем, что там настроено. «Бьёт» — моб у главного в цели и уже ранен, бьёт главного или его
/// пета, или главный кастует по нему; цель «просто выбрана» — не бьёт. Целей нет — помогает с мобом, который напал на главного.
/// Моба не видно у себя (далеко) — не просит. Ход не занимает: только просьба бою, бой стоит раньше по порядку.
/// </summary>
public sealed class AssistBehavior(LeaderBoard board) : IBehavior
{
    // Кого просили убить мы (чужую просьбу — заданий — не снимаем)
    private uint _asked;

    public string Name => "ассист";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var mob = c.Settings.Follow.Assist && Leader.Of(c, board) is { } leader ? Fought(leader) : null;
        var mine = mob is null ? null : c.World.Mobs.FirstOrDefault(m => m.Wid == mob.Wid && !m.IsDead);

        if (mine is null)
        {
            if (_asked != 0 && c.Fight.Prey == _asked)
                c.Fight.Prey = 0;
            _asked = 0;
            return false;
        }

        if (_asked != mine.Wid)
            c.Log.Info($"Ассист: {mine.Name} — бью вместе с главным");
        _asked = mine.Wid;
        c.Fight.Prey = mine.Wid;
        Status = $"ассист: {mine.Name}";
        return false;
    }

    /// <summary>Кого бьёт главный (по его снимку); null — никого.</summary>
    public static NpcInfo? Fought(WorldState leader)
    {
        var target = leader.Host.TargetWid;
        if (target != 0 && leader.Mobs.FirstOrDefault(m => m.Wid == target && !m.IsDead) is { } mob
            && (leader.AttacksUs(mob) || mob.MaxHp > 0 && mob.Hp < mob.MaxHp || leader.Host.IsCasting))
            return mob;
        return leader.Mobs.Where(leader.AttacksUs).OrderBy(m => m.Offset.Direct).FirstOrDefault();
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _asked = 0;
        Status = null;
    }
}

using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// «Следовать», «Городской портал»: главный начал кастовать портал (<see cref="World.HostState.CastingSkillId"/>) — кастуем и мы,
/// как кнопкой. Не изучен или в откате — пишем в лог и остаёмся. На один каст главного — одна попытка. Пока кастуем, ход держим:
/// шаг за главным, бой или «прервать каст без цели» сбили бы портал. Потом главный в городе — идём за ним как обычно.
/// </summary>
public sealed class FollowPortalBehavior(LeaderBoard board) : IBehavior
{
    // Этот каст главного уже обработали (ждём, пока он кончится)
    private bool _answered;

    public string Name => "портал за главным";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var skill = c.Skills.TownPortal;
        if (!c.Settings.Follow.Portal || skill == 0 || Leader.Of(c, board) is not { } leader)
            return false;

        if (c.World.Host.CastingSkillId == skill || c.Runner.IsPending(new ActionKey(ActionSlot.Skill, (uint)skill)))
        {
            Status = "портал за главным: кастую";
            return true;
        }

        if (leader.Host.CastingSkillId != skill)
        {
            _answered = false;
            return false;
        }
        if (_answered)
            return false;
        _answered = true;

        var mine = c.World.Skill(skill);
        if (mine is null)
        {
            c.Log.Warning("Главный кастует «Городской портал», а у меня его нет — остаюсь");
            return false;
        }
        if (!mine.IsReady)
        {
            c.Log.Info($"Главный кастует «Городской портал», у меня он в откате ({mine.CooldownLeftMs / 1000} с) — остаюсь");
            return false;
        }

        c.Log.Info("Главный кастует «Городской портал» — кастую тоже");
        foreach (var walking in c.Mine.OfType<MoveAction>().ToList())
            c.Forget(walking);
        c.Send(new SkillAction(skill, 0, approach: true, title: "Городской портал"));
        Status = "портал за главным: кастую";
        return true;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
        _answered = false;
        Status = null;
    }
}

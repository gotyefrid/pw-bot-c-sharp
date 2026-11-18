using Notes.Core.Actions;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// «Следовать», прыжки: у главного прыжков больше, чем у нас (<see cref="HostState.Jumps"/>: 0 — на земле, 1 — прыгнул, 2 —
/// двойной), — прыгаем сразу, без других условий: главный прыгнул — прыжок, прыгнул второй раз — второй. Прыжок ждёт, пока
/// счётчик вырастет (<see cref="JumpAction"/>), — за один прыжок главного не прыгаем дважды. В полёте не прыгаем. Ход не
/// занимает: прыгаем на бегу, бой и ходьба идут дальше.
/// </summary>
public sealed class FollowJumpBehavior(LeaderBoard board) : IBehavior
{
    public string Name => "прыжки за главным";
    public string? Status => null;

    public bool Tick(BrainContext c)
    {
        if (!c.Settings.Follow.Jump || Leader.Of(c, board) is not { } leader)
            return false;
        if (leader.Host.Jumps is not { } theirs || c.World.Host.Jumps is not { } mine || theirs <= mine)
            return false;
        if (c.World.Host.Flying == true || c.Runner.IsPending(ActionSlot.Jump))
            return false;

        c.Send(new JumpAction());
        return false;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset()
    {
    }
}

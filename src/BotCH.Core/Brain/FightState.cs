using BotCH.Core.World;

namespace BotCH.Core.Brain;

public enum FightPhase
{
    /// <summary>Боя нет — бой ищет цель.</summary>
    Idle,
    Fighting,
    /// <summary>Моб убит — подбираем лут вокруг места смерти.</summary>
    Looting,
}

/// <summary>
/// Общее состояние боя (<see cref="BrainContext.Fight"/>). Пишут бой (<see cref="CombatBehavior"/>) и лут (<see cref="LootBehavior"/>),
/// остальные поведения только читают — пет, копание, возврат ждут конца боя — или просят бросить бой (<see cref="Abort"/>:
/// уход от опасного моба). Так поведения не держат ссылок друг на друга: набор задаёт только их порядок.
/// </summary>
public sealed class FightState
{
    public FightPhase Phase { get; private set; }

    /// <summary>Идёт бой или лут.</summary>
    public bool Busy => Phase != FightPhase.Idle;

    /// <summary>Кого бьём (в бою) или кого убили (в луте); 0 — никого.</summary>
    public uint Mob { get; private set; }

    public string MobName { get; private set; } = "";

    /// <summary>Где умер моб — вокруг этого места ищем лут.</summary>
    public Position KilledAt { get; private set; }

    /// <summary>Номер убийства: новый — новый лут (счёт попыток с нуля).</summary>
    public int Kill { get; private set; }

    public void Start(NpcInfo mob)
    {
        Phase = FightPhase.Fighting;
        Mob = mob.Wid;
        MobName = mob.Name;
    }

    public void Killed(Position at)
    {
        Phase = FightPhase.Looting;
        KilledAt = at;
        Kill++;
    }

    /// <summary>Бой или лут кончились или брошены — снова ищем цель.</summary>
    public void Abort()
    {
        Phase = FightPhase.Idle;
        Mob = 0;
    }
}

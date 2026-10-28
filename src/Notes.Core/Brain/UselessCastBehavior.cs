using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;

namespace Notes.Core.Brain;

/// <summary>
/// Персонаж кастует атакующий скилл, а живой цели у него уже нет (моб умер, пропал, бот сам его бросил) — каст прервать:
/// бить некого, а всё остальное (лут, следующий моб, копание, возврат) ждёт конца каста. Скилл, заказанный по живому мобу,
/// игра бывает начинает уже после его смерти, поэтому смотрим на каждом снимке, а не в миг смерти. Прерываем раз за каст и
/// только атакующий скилл из настроек боя: лечение пета и прочее не трогаем; какой скилл кастуется, не видно — тоже.
/// Ход не занимает: стоит сразу за «выжить», чтобы смотреть, что бы бот ни делал дальше.
/// </summary>
public sealed class UselessCastBehavior : IBehavior
{
    private bool _cancelled;

    public string Name => "каст без цели";
    public string? Status => null;

    public bool Tick(BrainContext c)
    {
        var host = c.World.Host;
        if (!host.IsCasting)
        {
            _cancelled = false;
            return false;
        }

        var attack = c.Settings.Combat.AttackSkillId;
        if (_cancelled || attack == 0 || host.CastingSkillId != attack || c.World.Target is { IsDead: false }
            || !c.Runner.Capabilities.Has(Capability.Cancel))
            return false;

        if (c.Send(new CancelAction("каст")) == SubmitStatus.Sent)
        {
            _cancelled = true;
            c.Log.Info($"Живой цели нет — прерываю «{c.World.Skill(attack)?.Name ?? attack.ToString()}»");
        }

        return false;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void Reset() => _cancelled = false;
}

using System;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Обход ресурсов: напал опасный моб (<see cref="DangerZones"/>) — не драться, а уйти вверх: отозвать пета, взлететь (если на
/// земле) и подниматься рывками над своим местом, пока моб не отстанет — наземный не достанет, воздушный бросит погоню по
/// времени. Отстал — обход возвращается на последнюю посещённую точку (моб, скорее всего, отошёл — ресурс мог освободиться).
/// Не вышло (полёта нет, моб бьёт дольше <see cref="GiveUpAfter"/>, поднялись на <see cref="MaxClimb"/>) — дерёмся, как с любым
/// напавшим, пока этот моб не отстанет. Стоит сразу после выживания: банки пьём и при подъёме.
/// </summary>
public sealed class EscapeBehavior(RouteBehavior route, CombatBehavior combat) : IBehavior
{
    private const float ClimbStep = 20f;
    private const float MaxClimb = 200f;
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);

    private NpcInfo? _from;
    private DateTime _since;
    private float _startHeight;
    private float _climbed;
    private bool _recallTried;
    private bool _fighting;
    private MoveAction? _climb;

    public string Name => "уход";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var w = c.World;
        var threat = Threat(c);
        if (threat is null)
        {
            if (_from is { } was)
            {
                c.Log.Info(_fighting
                    ? $"{was.Name} отстал — возвращаюсь на точку"
                    : $"{was.Name} отстал на высоте +{w.Host.Position.Height - _startHeight:0} м — возвращаюсь на точку");
                // Недолетевший подъём держал бы тело — обход не смог бы лететь к точке
                if (_climb is not null)
                    c.Runner.Forget(_climb);
                route.BackToLastVisited(c);
            }
            Forget();
            return false;
        }

        if (_from is null)
        {
            (_from, _since, _startHeight, _climbed, _recallTried, _fighting, _climb) = (threat, c.Now, w.Host.Position.Height, 0, false, false, null);
            combat.Reset();
            c.Log.Warning($"Напал опасный {threat.Name} (ур. {threat.Level}) — улетаю вверх");
        }

        if (_fighting)
            return false;

        _climbed = w.Host.Position.Height - _startHeight;
        if (w.Host.Flying is null || c.Now - _since > GiveUpAfter || _climbed >= MaxClimb)
        {
            GiveUp(c, w.Host.Flying is null ? "не знаем, летит ли персонаж" : _climbed >= MaxClimb ? $"поднялись на {_climbed:0} м" : $"бьёт {GiveUpAfter.TotalSeconds:0} с");
            return false;
        }

        // Пет не должен остаться драться один
        if (w.Pet is { IsSummoned: true } && c.Runner.Actions.CanRecallPet && !_recallTried)
        {
            Status = "отзываю пета";
            var recall = c.Send(new RecallPetAction { Priority = ActionPriority.Urgent });
            _recallTried = recall is not SubmitStatus.Busy;
            if (recall is SubmitStatus.Sent or SubmitStatus.AlreadyPending)
                return true;
        }

        if (w.Host.Flying == false)
        {
            Status = $"взлетаю — напал {threat.Name}";
            return c.Submit(new FlyAction(up: true) { Priority = ActionPriority.Urgent });
        }

        Status = $"ухожу вверх от {threat.Name}, +{_climbed:0} м";
        if (_climb is not null && c.Runner.Pending.Contains(_climb))
            return true;

        var p = w.Host.Position;
        _climb = new MoveAction(new Position(p.X, p.Height + ClimbStep, p.Y), 2f, fly: true) { Priority = ActionPriority.Urgent };
        var sent = c.Runner.Replace(_climb, w).Status;
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    // Опасный моб бьёт перса или пета (пока уходим — тот же, даже если переключился)
    private static NpcInfo? Threat(BrainContext c)
    {
        var w = c.World;
        var petWid = w.Pet?.ActiveWid ?? 0;
        return w.Mobs
            .Where(m => !m.IsDead && m.TargetWid != 0 && (m.TargetWid == w.Host.Wid || m.TargetWid == petWid)
                        && DangerZones.IsDangerous(m, c.Settings.Route))
            .OrderBy(m => m.Distance)
            .FirstOrDefault();
    }

    private void GiveUp(BrainContext c, string why)
    {
        _fighting = true;
        c.Log.Warning($"Не ушёл от {_from!.Name} ({why}) — дерусь");
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (_from is null || _fighting || outcome.Status != ActionStatus.Failed)
            return;
        // Взлёт или подъём не выходит (нет полётника, нельзя в воде…) — значит, бой
        if (outcome.Action is FlyAction || outcome.Action == _climb)
            GiveUp(c, outcome.Details);
    }

    private void Forget()
    {
        _from = null;
        _fighting = false;
        _climb = null;
    }

    public void Reset()
    {
        Forget();
        Status = null;
    }
}

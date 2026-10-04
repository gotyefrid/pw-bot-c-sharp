using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.World;

namespace BotCH.Core.Brain;

/// <summary>
/// Обход ресурсов: напал опасный моб (<see cref="DangerZones"/>) — не драться, а уйти вверх: отозвать пета, взлететь (если на
/// земле) и подниматься шагами по <see cref="ClimbStep"/> над своим местом, пока моб бьёт; перестал бить — висим. Отагр видим
/// сразу: моб «возвращается» (<see cref="NpcInfo.Returning"/>) или сбросил цель — тогда обход возвращается на последнюю
/// посещённую точку (моб, скорее всего, отошёл — ресурс мог освободиться). Цель у моба может застрять на нас (клиент не получил
/// отагр) — не бьёт <see cref="StaleAfter"/> — считаем отставшим и не трогаем его, пока снова не ударит.
/// Не вышло (полёта нет, моб всё бьёт через <see cref="GiveUpAfter"/>, поднялись на <see cref="MaxClimb"/>) — дерёмся, как с любым
/// напавшим, пока этот моб не отстанет. Стоит сразу после выживания: банки пьём и при подъёме.
/// </summary>
public sealed class EscapeBehavior(RouteBehavior route, CombatBehavior combat) : IBehavior
{
    private const float ClimbStep = 10f;
    private const float MaxClimb = 200f;
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);
    // Ударил недавно — значит, всё ещё бьёт (между ударами моб «стоит» или «идёт»)
    private static readonly TimeSpan HitMemory = TimeSpan.FromSeconds(3);

    private NpcInfo? _from;
    private DateTime _since;
    private DateTime _lastHit;
    private float _startHeight;
    private bool _recallTried;
    private bool _fighting;
    private MoveAction? _climb;
    // Свой взлёт: по его итогу (не по чужому взлёту маршрута) решаем, уходить или драться
    private FlyAction? _fly;
    // Отстали, а цель застряла на нас: не трогаем, пока снова не ударит
    private readonly HashSet<uint> _shaken = [];

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
                Shaken(c, was, w.Npcs.FirstOrDefault(n => n.Wid == was.Wid) is { } now
                    ? now.Returning ? "возвращается" : "бросил цель"
                    : "пропал из виду");
            return false;
        }

        // Начинаем уходить, только если моб действует (бьёт, кастует, идёт к нам): стоящий с нашей целью — застрявшая цель
        if (_from is null && !threat.Engaging)
            return false;
        if (_from is null || _from.Wid != threat.Wid && !_fighting)
        {
            (_from, _since, _lastHit, _startHeight, _recallTried, _fighting, _climb, _fly) = (threat, c.Now, c.Now, w.Host.Position.Height, false, false, null, null);
            combat.Reset();
            c.Log.Warning($"Напал опасный {threat.Name} (ур. {threat.Level}) — улетаю вверх");
        }

        if (_fighting)
            return false;

        if (Hits(threat))
            _lastHit = c.Now;
        var climbed = w.Host.Position.Height - _startHeight;
        if (w.Host.Flying is null)
            return GiveUp(c, "не знаем, летит ли персонаж");
        if (climbed >= MaxClimb)
            return GiveUp(c, $"поднялись на {climbed:0} м");
        if (c.Now - _since > GiveUpAfter && c.Now - _lastHit < HitMemory)
            return GiveUp(c, $"бьёт {GiveUpAfter.TotalSeconds:0} с");
        if (c.Now - _lastHit > StaleAfter)
        {
            // Цель застряла на нас (клиент не узнал об отагре), а моб давно не бьёт — отстал
            _shaken.Add(threat.Wid);
            Shaken(c, threat, $"не бьёт {StaleAfter.TotalSeconds:0} с");
            return false;
        }

        // Пет не должен остаться драться один
        if (w.Pet is { IsSummoned: true } && c.Runner.Capabilities.Has(Capability.RecallPet) && !_recallTried)
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
            var fly = new FlyAction(up: true) { Priority = ActionPriority.Urgent };
            var takeoff = c.Send(fly);
            // Свой — и ушедший, и не отправленный (его отказ тоже придёт в OnOutcome); «уже ждёт» — это прежний свой или взлёт
            // маршрута (второй раз «Полёт» не жмём: он бы взлёт отменил)
            if (takeoff is SubmitStatus.Sent or SubmitStatus.Failed)
                _fly = fly;
            return takeoff is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
        }

        if (_climb is not null && c.Mine.Contains(_climb))
        {
            Status = $"ухожу вверх от {threat.Name}, +{climbed:0} м";
            return true;
        }

        // Бьёт (или только что бил) — выше; не бьёт — висим, ждём отагра
        if (c.Now - _lastHit >= HitMemory)
        {
            Status = $"вишу на +{climbed:0} м — жду, пока {threat.Name} отстанет";
            return true;
        }

        var p = w.Host.Position;
        _climb = new MoveAction(new Position(p.X, p.Height + ClimbStep, p.Y), 2f, fly: true) { Priority = ActionPriority.Urgent };
        Status = $"ухожу вверх от {threat.Name}, +{climbed:0} м";
        var sent = c.Replace(_climb);
        return sent is SubmitStatus.Sent or SubmitStatus.AlreadyPending;
    }

    private static bool Hits(NpcInfo mob) => mob.State is NpcState.Attacking or NpcState.Casting;

    // Опасный моб бьёт перса или пета: не возвращается (тот уже не наш), не «отставший» с застрявшей целью — пока снова не ударит
    private NpcInfo? Threat(BrainContext c)
    {
        var w = c.World;
        var petWid = w.Pet?.ActiveWid ?? 0;
        foreach (var back in w.Mobs.Where(m => _shaken.Contains(m.Wid) && (Hits(m) || m.TargetWid != w.Host.Wid && m.TargetWid != petWid)).ToList())
            _shaken.Remove(back.Wid);
        return w.Mobs
            .Where(m => w.TargetsUs(m) && !_shaken.Contains(m.Wid) && DangerZones.IsDangerous(m, c.Settings.Route))
            .OrderBy(m => m.Offset.Horizontal)
            .FirstOrDefault();
    }

    // Отстал — недолетевший подъём забываем (держал бы тело) и снова к последней посещённой точке
    private void Shaken(BrainContext c, NpcInfo mob, string how)
    {
        c.Log.Info(_fighting
            ? $"{mob.Name} отстал ({how}) — возвращаюсь на точку"
            : $"{mob.Name} отстал ({how}) на высоте +{c.World.Host.Position.Height - _startHeight:0} м — возвращаюсь на точку");
        if (_climb is not null)
            c.Forget(_climb);
        route.BackToLastVisited(c);
        Forget();
    }

    private bool GiveUp(BrainContext c, string why)
    {
        _fighting = true;
        c.Log.Warning($"Не ушёл от {_from!.Name} ({why}) — дерусь");
        return false;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (_from is null || _fighting)
            return;
        // Только свои действия: взлёт маршрута — не наш. Взлёт не вышел — значит, бой. Без полётника клиент на кнопку
        // «Полёт» молча ничего не делает: вызов проходит, а взлёта нет — итог «нет подтверждения» за 5 с, не «не отправлено»
        if (outcome.Action == _fly && outcome.Status is ActionStatus.Failed or ActionStatus.Rejected or ActionStatus.Timeout)
            GiveUp(c, outcome.Details);
        // Подъём не отправился (не в воздухе, нельзя лететь в точку)
        else if (outcome.Action == _climb && outcome.Status == ActionStatus.Failed)
            GiveUp(c, outcome.Details);
    }

    private void Forget()
    {
        _from = null;
        _fighting = false;
        _climb = null;
        _fly = null;
    }

    public void Reset()
    {
        Forget();
        _shaken.Clear();
        Status = null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain;

public enum PullStep
{
    /// <summary>Собираем: ход занят.</summary>
    Pulling,
    /// <summary>Собрали сколько нужно — или больше рядом нет, но хоть один есть.</summary>
    Done,
    /// <summary>Рядом нет ни одного подходящего моба.</summary>
    NoMobs,
}

/// <summary>
/// Собрать на себя мобов — по одному выстрелу в каждого. Один сбор — один объект: в кого стреляли, помнит только он.
/// <list type="bullet">
/// <item>Ближайший подходящий моб (<c>allowed</c>: где обитает), в которого в этом сборе ещё не стреляли, — как далеко бы ни был;
/// <c>inSpace</c> — ближний в пространстве, иначе по горизонтали. Бьёт ли он нас — не смотрим: после воскрешения
/// у старых мобов в цели ещё остаётся перс, хотя они его уже не бьют.</item>
/// <item>Выстрел удачный, когда в цели этот моб и скилл ушёл на перезарядку — сразу следующий; не
/// ушёл (каст сбили) — ещё раз, до <see cref="ShotsPerMob"/>. Скилл — атакующий из настроек боя, иначе основной у класса;
/// нет ни того, ни другого — обычная атака, удачная, только когда моб взял перса в цель.</item>
/// <item>В цель не берётся <see cref="SelectTries"/> раз — следующий (было: перс висел в воздухе и выбирал одну пчелу 6,5 часа).</item>
/// </list>
/// Не поведение, а деталь поведения: шаги — из его Tick, действия уходят от его имени.
/// </summary>
public sealed class MobPull(int need, Func<NpcInfo, bool> allowed, bool inSpace)
{
    public const int ShotsPerMob = 10;

    /// <summary>Столько раз моб не взялся в цель — берём другого.</summary>
    public const int SelectTries = 5;

    // Выстрел: ждём, пока скилл уйдёт на перезарядку (с подбеганием клиента); дольше — каст сбит
    private static readonly TimeSpan ShotWait = TimeSpan.FromSeconds(5);
    // Обычная атака перезарядки не даёт: засчитываем, когда моб взял перса в цель; ждём дольше — перс бежит к мобу
    private static readonly TimeSpan SwordWait = TimeSpan.FromSeconds(10);
    // Скилл перезаряжается дольше этого — стреляем всё равно (отказ покажет причину в логе)
    private static readonly TimeSpan ReadyWait = TimeSpan.FromSeconds(6);

    private readonly HashSet<uint> _shot = [];
    private uint _mob;
    private int _shots;
    private int _selects;
    private GameAction? _shotAction;
    private DateTime _shotAt;

    /// <summary>Сколько уже собрали.</summary>
    public int Pulled { get; private set; }

    public string Status => $"собираю мобов {Pulled}/{need}";

    public PullStep Step(BrainContext c)
    {
        var w = c.World;
        if (Pulled >= need)
            return Finish(c);

        var mob = _mob == 0 ? null : w.Mobs.FirstOrDefault(m => m.Wid == _mob && !m.IsDead);
        if (mob is null)
        {
            Drop(c);
            mob = NextMob(c);
            if (mob is null)
                return Pulled > 0 ? Finish(c) : PullStep.NoMobs;

            _mob = mob.Wid;
            _shots = 0;
            _selects = 0;
            _shot.Add(mob.Wid);
            c.Log.Debug($"Сбор мобов: беру {mob.Name} ур. {mob.Level}, {mob.Offset.Horizontal:0} м ({Pulled + 1}/{need})");
        }

        if (w.Host.TargetWid != mob.Wid)
        {
            Drop(c);
            // Попытка — когда прошлая кончилась (подтверждения нет 2 с или отказ клиента)
            if (c.Mine.OfType<SelectTargetAction>().Any(a => a.Npc.Wid == mob.Wid))
                return PullStep.Pulling;
            if (_selects >= SelectTries)
            {
                c.Log.Debug($"Сбор мобов: {mob.Name} {SelectTries} раз не взялся в цель — беру другого");
                _mob = 0;
                return PullStep.Pulling;
            }
            if (c.Send(new SelectTargetAction(mob)) is SubmitStatus.Sent or SubmitStatus.Failed)
                _selects++;
            return PullStep.Pulling;
        }

        var skill = AttackSkill(c);
        if (_shotAction is not null)
        {
            // Скиллом — по перезарядке: цель у моба на персе бывает и старая (после воскрешения), она не показывает, что попали
            var hit = skill == 0 ? mob.TargetWid == w.Host.Wid : w.Skill(skill) is { IsReady: false };
            if (hit)
            {
                Drop(c);
                Pulled++;
                _mob = 0;
                c.Log.Debug($"Сбор мобов: выстрел по {mob.Name} ушёл ({Pulled}/{need})");
                return PullStep.Pulling;
            }
            if (c.Now - _shotAt < (skill == 0 ? SwordWait : ShotWait))
                return PullStep.Pulling;

            Drop(c);
            if (++_shots >= ShotsPerMob)
            {
                c.Log.Debug($"Сбор мобов: по {mob.Name} {ShotsPerMob} выстрелов не ушли — беру другого");
                _mob = 0;
                return PullStep.Pulling;
            }
        }

        // Перезарядка (прошлый выстрел): ждём, но не вечно
        if (skill != 0 && w.Skill(skill) is { IsReady: false } && c.Now - _shotAt < ReadyWait)
            return PullStep.Pulling;

        GameAction shot = skill == 0 ? new NormalAttackAction() : new SkillAction(skill, mob.Wid, approach: true);
        if (c.Send(shot) is SubmitStatus.Sent or SubmitStatus.AlreadyPending)
        {
            _shotAction = shot;
            _shotAt = c.Now;
        }
        else
        {
            // Не ушёл совсем (отказ клиента) — считаем попыткой, чтобы не стоять на одном мобе
            _shotAt = c.Now;
            if (++_shots >= ShotsPerMob)
                _mob = 0;
        }
        return PullStep.Pulling;
    }

    /// <summary>Бросить сбор: итога выстрела не ждём, цель снимаем.</summary>
    public void Drop(BrainContext c)
    {
        if (_shotAction is not null)
            c.Forget(_shotAction);
        _shotAction = null;
    }

    // Собрали: цель снять, чтобы перс не бил сам (амулет и так тянет время)
    private PullStep Finish(BrainContext c)
    {
        Drop(c);
        _mob = 0;
        if (c.World.Host.TargetWid != 0)
            c.Submit(new UnselectAction());
        return PullStep.Done;
    }

    private NpcInfo? NextMob(BrainContext c)
        => c.World.Mobs
            .Where(m => m.Kind == NpcKind.Mob && !m.IsDead && !m.Returning && !_shot.Contains(m.Wid) && allowed(m))
            .OrderBy(m => inSpace ? c.World.Host.Position.DistanceTo(m.Position) : m.Offset.Horizontal)
            .FirstOrDefault();

    // Атакующий скилл боя (даже если в бою бьёт пет и «Бить скиллом» выключено: издалека подманивать быстрее), иначе основной
    // атакующий скилл класса — если он у перса есть; 0 — обычная атака
    private static int AttackSkill(BrainContext c)
    {
        foreach (var skill in new[] { c.Settings.Combat.AttackSkillId, c.Skills.DefaultAttack })
            if (skill != 0 && c.World.Skill(skill) is not null)
                return skill;
        return 0;
    }
}

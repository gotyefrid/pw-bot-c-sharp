using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Слив опыта смертью (<see cref="Settings.ExpDrainSettings"/>): на уровне «не выше» при опыте от порога — собрать на себя
/// мобов, умереть, встать в ближнем городе, прилететь обратно и так, пока опыт не станет не больше цели. Амулет раз в 10 с
/// восстанавливает всё HP, поэтому мобов несколько.
/// <list type="bullet">
/// <item>Стоит в наборе почти первым: пока слив идёт, ход всегда у него — банки, пет, бой, лут, копание и возврат не ходят и
/// ему не мешают. Начинает только без боя (бой добивает моба, лут собирается).</item>
/// <item>Сбор: ближайший разрешённый моб до <see cref="PullRadius"/> м, в которого в этом круге ещё не стреляли; на земле —
/// только мобы на нашей высоте (летающие не подманиваются). Выстрел удачный, когда в цели этот моб и скилл ушёл на
/// перезарядку — сразу следующий; не ушёл (каст сбили) — ещё раз, до <see cref="ShotsPerMob"/>.</item>
/// <item>Перс начал слив в воздухе (фарм летающих мобов) — не садится: собирает и умирает в полёте, возвращается на ту же высоту.</item>
/// <item>Смерть — «Ближний город» (<see cref="IDeathBehavior"/>: мозг не останавливает бота), в лог — сколько опыта снято.
/// Обратно: взлёт, подъём строго вверх до высоты полёта, отрезки по прямой, над точкой — посадка.</item>
/// </list>
/// </summary>
public sealed class ExpDrainBehavior : IDeathBehavior
{
    /// <summary>Дальше мобов не собираем: подбегать к далёким долго, а стоять бот будет на месте.</summary>
    public const float PullRadius = 40f;

    /// <summary>На земле — моб выше или ниже перса больше этого: летающий, на выстрел не подманивается.</summary>
    public const float GroundHeight = 5f;

    public const int ShotsPerMob = 3;

    // Выстрел: ждём, пока скилл уйдёт на перезарядку (с подбеганием клиента); дольше — каст сбит
    private static readonly TimeSpan ShotWait = TimeSpan.FromSeconds(5);
    // Обычная атака перезарядки не даёт: удар засчитываем через столько после отправки
    private static readonly TimeSpan SwordWait = TimeSpan.FromSeconds(2);
    // Скилл перезаряжается дольше этого — стреляем всё равно (отказ покажет причину в логе)
    private static readonly TimeSpan ReadyWait = TimeSpan.FromSeconds(6);

    /// <summary>Не убили за столько после сбора — собираем ещё мобов.</summary>
    public static readonly TimeSpan DeathWait = TimeSpan.FromSeconds(60);

    /// <summary>Сколько раз собирать за одну жизнь, прежде чем бросить слив.</summary>
    public const int PullRounds = 3;

    /// <summary>Рядом нет мобов столько — бросаем слив.</summary>
    public static readonly TimeSpan NoMobsLimit = TimeSpan.FromSeconds(60);

    /// <summary>Брошенный слив пробуем снова через столько.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    /// <summary>После смерти до «Ближнего города» (окно смерти появляется не сразу).</summary>
    public static readonly TimeSpan ReviveDelay = TimeSpan.FromSeconds(2);

    /// <summary>Встали в городе — пауза до взлёта (мир вокруг подгружается).</summary>
    public static readonly TimeSpan AfterRevive = TimeSpan.FromSeconds(2.5);

    private static readonly TimeSpan ReviveRetry = TimeSpan.FromSeconds(5);
    private const int ReviveAttempts = 3;

    /// <summary>Столько смертей подряд не сняли опыт — слив выключаем до перезапуска бота.</summary>
    public const int NoLossDeaths = 2;

    /// <summary>Над точкой — ближе этого по горизонтали (полёт останавливается в ~3 м от точки отрезка).</summary>
    public const float Arrived = 5f;

    /// <summary>Отрезок полёта: дальше за раз не летим (полёт в точку на сотни метров игра ведёт неровно).</summary>
    public const float LegLength = 150f;

    // Не долетели — поднимаемся выше на столько (в памяти: 2 м как в игре) и пробуем снова, не больше FlightFails раз
    private const float FailClimb = 20f;
    private const int FlightFails = 5;
    // Над городом бывает выше заданной высоты: во время полёта ниже неё не опускаемся, но и не выше этого запаса
    private const float KeepHigher = 50f;

    /// <summary>Дальше места фарма — видимо, город другой или точка с другой локации: не летим.</summary>
    public const float MaxTrip = 3000f;

    private enum Phase { Off, RecallPet, Pull, WaitDeath, Dead, Revived, Return }

    private Phase _phase;
    private bool _air;
    private Position _home;
    private string? _off;
    private DateTime _retryAt = DateTime.MinValue;

    // Сбор
    private readonly HashSet<uint> _shot = [];
    private int _pulled;
    private int _rounds;
    private uint _mob;
    private int _shots;
    private GameAction? _shotAction;
    private DateTime _shotAt;
    private DateTime _phaseSince;
    private DateTime? _noMobsSince;

    // Смерть
    private int? _expAlive;
    private double? _percentAlive;
    private int? _expBeforeDeath;
    private double? _percentBeforeDeath;
    private int _noLoss;
    private int _reviveTries;
    private DateTime _reviveAt;

    // Возврат
    private bool _climbed;
    private float _cruise;
    private int _fails;
    private bool _walk;
    private bool _finishAfterReturn;

    public string Name => "слив опыта";
    public string? Status { get; private set; }

    /// <summary>Слив идёт (для тестов и окна).</summary>
    public bool Active => _phase != Phase.Off;

    public bool Tick(BrainContext c)
    {
        Status = null;
        var host = c.World.Host;
        var settings = c.Settings.ExpDrain;
        if (!settings.Enabled)
        {
            if (_phase != Phase.Off)
            {
                c.Log.Info("Слив опыта выключен — фармлю дальше");
                Stop(c);
            }
            return false;
        }

        _expAlive = host.Exp;
        _percentAlive = host.ExpPercent;
        return _phase switch
        {
            Phase.Off => TryStart(c),
            Phase.RecallPet => RecallPet(c),
            Phase.Pull => Pull(c),
            Phase.WaitDeath => WaitDeath(c),
            Phase.Dead => Revived(c),
            Phase.Revived => AfterRevival(c),
            _ => Return(c),
        };
    }

    // ── Начало ───────────────────────────────────────────────────────────────

    private bool TryStart(BrainContext c)
    {
        var host = c.World.Host;
        var settings = c.Settings.ExpDrain;
        if (_off is not null || c.Now < _retryAt || host.Level < settings.MaxLevel)
            return false;
        if (host.Level > settings.MaxLevel)
        {
            c.Say("drain-late", $"Слив опыта: уже {host.Level} ур., выше {settings.MaxLevel} — поздно, уровень смертью не снять", LogLevel.Warning, 3600);
            return false;
        }
        if (host.ExpPercent is not { } percent || percent < settings.StartPercent)
            return false;
        if (WhyNot(c) is { } why)
        {
            c.Say("drain-unsupported", $"Слив опыта на этом сервере не работает: {why}", LogLevel.Warning, 3600);
            return false;
        }
        // Добить моба и собрать лут; копание, каст и банк — дождаться
        if (c.Fight.Busy || c.Runner.BodyBusy(c.World) is not null)
            return false;

        _air = host.Flying == true;
        _home = _air ? host.Position : c.FarmCenter ?? host.Position;
        _noLoss = 0;
        c.Fight.Abort();
        c.Log.Info($"Опыт {percent:0.0} % на {host.Level} ур. (порог {settings.StartPercent} %) — сливаю до {settings.TargetPercent} %: "
                   + $"собираю {settings.Mobs} мобов и умираю{(_air ? " в воздухе" : "")}");
        StartPull(c);
        _phase = Phase.RecallPet;
        return RecallPet(c);
    }

    private static string? WhyNot(BrainContext c)
    {
        var caps = c.Runner.Capabilities;
        if (c.World.Host.ExpToLevel is null)
            return "не знаем, сколько опыта до уровня";
        if (c.World.Host.Flying is null)
            return "не знаем, летит ли персонаж";
        return caps.WhyNot(Capability.ReviveInTown) ?? caps.WhyNot(Capability.FlyTo);
    }

    private bool RecallPet(BrainContext c)
    {
        Status = "слив опыта: отзываю пета";
        if (c.Mine.OfType<RecallPetAction>().Any())
            return true;
        if (c.World.Pet is { IsSummoned: true } && c.Runner.Capabilities.Has(Capability.RecallPet)
            && c.Send(new RecallPetAction()) == SubmitStatus.Sent)
        {
            c.Log.Info("Слив опыта: отзываю пета");
            return true;
        }

        _phase = Phase.Pull;
        _phaseSince = c.Now;
        return Pull(c);
    }

    // ── Сбор мобов ───────────────────────────────────────────────────────────

    private void StartPull(BrainContext c)
    {
        _shot.Clear();
        _pulled = 0;
        _rounds = 0;
        _mob = 0;
        _noMobsSince = null;
        _phaseSince = c.Now;
    }

    private bool Pull(BrainContext c)
    {
        var w = c.World;
        var need = c.Settings.ExpDrain.Mobs;
        Status = $"слив опыта: собираю мобов {_pulled}/{need}";
        if (_pulled >= need)
            return StartWaiting(c, $"собрал {_pulled} мобов");

        var mob = _mob == 0 ? null : w.Mobs.FirstOrDefault(m => m.Wid == _mob && !m.IsDead);
        if (mob is null)
        {
            DropShot(c);
            mob = NextMob(c);
            if (mob is null)
            {
                if (_pulled > 0)
                    return StartWaiting(c, $"рядом больше нет мобов, собрал {_pulled}");
                _noMobsSince ??= c.Now;
                if (c.Now - _noMobsSince >= NoMobsLimit)
                    return Abandon(c, $"рядом ({PullRadius:0} м) нет мобов {NoMobsLimit.TotalSeconds:0} с");
                Status = "слив опыта: жду мобов рядом";
                return true;
            }

            _noMobsSince = null;
            _mob = mob.Wid;
            _shots = 0;
            _shot.Add(mob.Wid);
            c.Log.Debug($"Слив опыта: беру {mob.Name} ур. {mob.Level}, {mob.Offset.Horizontal:0} м ({_pulled + 1}/{need})");
        }

        if (w.Host.TargetWid != mob.Wid)
        {
            DropShot(c);
            c.Submit(new SelectTargetAction(mob));
            return true;
        }

        var skill = AttackSkill(c);
        if (_shotAction is not null)
        {
            var hit = skill == 0 ? c.Now - _shotAt >= SwordWait : w.Skill(skill) is { IsReady: false };
            if (hit)
            {
                DropShot(c);
                _pulled++;
                _mob = 0;
                c.Log.Debug($"Слив опыта: выстрел по {mob.Name} ушёл ({_pulled}/{need})");
                return true;
            }
            if (c.Now - _shotAt < ShotWait)
                return true;

            DropShot(c);
            if (++_shots >= ShotsPerMob)
            {
                c.Log.Debug($"Слив опыта: по {mob.Name} {ShotsPerMob} выстрела не ушли — беру другого");
                _mob = 0;
                return true;
            }
        }

        // Перезарядка (прошлый выстрел): ждём, но не вечно
        if (skill != 0 && w.Skill(skill) is { IsReady: false } && c.Now - _shotAt < ReadyWait)
            return true;

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
        return true;
    }

    // Ближайший разрешённый моб, в которого в этом круге не стреляли. Бьёт ли он нас — не смотрим: после воскрешения у старых
    // мобов в цели ещё остаётся перс, хотя они его уже не бьют
    private NpcInfo? NextMob(BrainContext c)
        => c.World.Mobs
            .Where(m => TargetSelector.IsAllowed(m, c.Settings.Target) && !m.Returning && !_shot.Contains(m.Wid)
                        && m.Offset.Horizontal <= PullRadius && (_air || Math.Abs(m.Offset.Vertical) <= GroundHeight))
            .OrderBy(m => _air ? c.World.Host.Position.DistanceTo(m.Position) : m.Offset.Horizontal)
            .FirstOrDefault();

    // Атакующий скилл боя, если он у перса есть; 0 — обычная атака
    private static int AttackSkill(BrainContext c)
    {
        var combat = c.Settings.Combat;
        return combat.UseSkill && combat.AttackSkillId != 0 && c.World.Skill(combat.AttackSkillId) is not null ? combat.AttackSkillId : 0;
    }

    // Итог выстрела игры не ждём: следующий выстрел не должен упираться в этот
    private void DropShot(BrainContext c)
    {
        if (_shotAction is not null)
            c.Forget(_shotAction);
        _shotAction = null;
    }

    private bool StartWaiting(BrainContext c, string why)
    {
        DropShot(c);
        _mob = 0;
        _phase = Phase.WaitDeath;
        _phaseSince = c.Now;
        c.Log.Info($"Слив опыта: {why} — стою и жду смерти");
        if (c.World.Host.TargetWid != 0)
            c.Submit(new UnselectAction());
        return true;
    }

    private bool WaitDeath(BrainContext c)
    {
        var w = c.World;
        Status = $"слив опыта: жду смерти, бьют {w.Mobs.Count(m => w.TargetsUs(m) && m.TargetWid == w.Host.Wid)}";
        if (c.Now - _phaseSince < DeathWait)
            return true;

        if (++_rounds >= PullRounds)
            return Abandon(c, $"не убили за {PullRounds} сбора");
        c.Log.Info($"Слив опыта: не убили за {DeathWait.TotalSeconds:0} с — собираю ещё мобов");
        _pulled = 0;
        _mob = 0;
        _noMobsSince = null;
        _phase = Phase.Pull;
        _phaseSince = c.Now;
        return true;
    }

    // ── Смерть ───────────────────────────────────────────────────────────────

    public bool TickDead(BrainContext c)
    {
        // Умер не во время слива (или слив выключили) — смерть не наша: бот остановится
        if (_phase == Phase.Off || !c.Settings.ExpDrain.Enabled)
            return false;

        if (_phase != Phase.Dead)
        {
            DropShot(c);
            _phase = Phase.Dead;
            _expBeforeDeath = _expAlive;
            _percentBeforeDeath = _percentAlive;
            _reviveTries = 0;
            _reviveAt = c.Now + ReviveDelay;
            c.Log.Info("Слив опыта: перс умер — встаю в ближнем городе");
        }

        Status = "слив опыта: встаю в ближнем городе";
        if (c.Mine.OfType<ReviveInTownAction>().Any() || c.Now < _reviveAt)
            return true;
        if (_reviveTries >= ReviveAttempts)
        {
            c.Say("drain-revive", $"Слив опыта: «Ближний город» не сработал {ReviveAttempts} раза", LogLevel.Warning);
            return false;
        }

        _reviveTries++;
        _reviveAt = c.Now + ReviveRetry;
        c.Send(new ReviveInTownAction());
        return true;
    }

    // Первый живой снимок после смерти: сколько опыта сняли, сливать ли дальше
    private bool Revived(BrainContext c)
    {
        var host = c.World.Host;
        var lost = _expBeforeDeath is { } before && host.Exp is { } now ? before - now : (int?)null;
        c.Log.Info(lost is null ? "Слив опыта: встал, сколько опыта снято — не знаем"
            : $"Слив опыта: встал, смерть сняла {lost} опыта ({_percentBeforeDeath:0.0} % → {host.ExpPercent:0.0} %)");
        if (lost is <= 0)
        {
            if (++_noLoss >= NoLossDeaths)
            {
                _off = $"{NoLossDeaths} смерти подряд не сняли опыт";
                c.Log.Warning($"Слив опыта: {_off} — выключаю слив до перезапуска бота, возвращаюсь фармить");
            }
        }
        else
            _noLoss = 0;

        _phase = Phase.Revived;
        _phaseSince = c.Now;
        Status = "слив опыта: встал в городе";
        return true;
    }

    private bool AfterRevival(BrainContext c)
    {
        Status = "слив опыта: встал в городе";
        if (c.Now - _phaseSince < AfterRevive)
            return true;

        _finishAfterReturn = _off is not null || c.World.Host.ExpPercent is not { } percent || percent <= c.Settings.ExpDrain.TargetPercent;
        var host = c.World.Host.Position;
        var distance = host.HorizontalDistanceTo(_home);
        if (distance > MaxTrip)
        {
            Stop(c);
            return c.RequestStop($"место фарма в {distance:0} м от города — не лечу");
        }

        c.Log.Info($"Слив опыта: {(_finishAfterReturn ? "опыт слит — " : "")}лечу на место фарма, {distance:0} м");
        _phase = Phase.Return;
        _climbed = false;
        _fails = 0;
        _walk = false;
        _cruise = Math.Max(c.Settings.ExpDrain.FlightHeight * 10, _air ? _home.Height : 0);
        return Return(c);
    }

    // ── Возврат ──────────────────────────────────────────────────────────────

    private bool Return(BrainContext c)
    {
        Status = "слив опыта: лечу на место фарма";
        if (c.Mine.Count > 0)
            return true;

        var host = c.World.Host;
        var here = host.Position;
        var left = here.HorizontalDistanceTo(_home);
        if (left <= Arrived && (!_air || Math.Abs(here.Height - _home.Height) <= Arrived))
        {
            // На земле фармили — садимся; в воздухе — остаёмся
            if (!_air && host.Flying == true)
            {
                Status = "слив опыта: сажусь";
                c.Submit(new FlyAction(up: false));
                return true;
            }
            return Arrive(c);
        }

        if (_walk)
            return c.Submit(new MoveAction(_home, tolerance: 3f, smart: true));
        if (host.Flying != true)
            return c.Submit(new FlyAction(up: true));

        // Сначала набрать высоту на месте, строго вверх, — подъём по пути может врезаться в дом или гору
        if (!_climbed)
        {
            _climbed = true;
            if (here.Height < _cruise - 2)
                return c.Submit(new MoveAction(here with { Height = _cruise }, tolerance: 2f, fly: true));
        }

        MoveAction leg;
        if (left <= Arrived)
            leg = new MoveAction(_home, tolerance: 2f, fly: true); // в воздухе — опуститься на высоту места
        else
        {
            var k = Math.Min(1f, LegLength / left);
            var height = Math.Max(_cruise, Math.Min(here.Height, _cruise + KeepHigher));
            var to = left <= LegLength && _air ? _home with { Height = height }
                : new Position(here.X + (_home.X - here.X) * k, height, here.Y + (_home.Y - here.Y) * k);
            leg = new MoveAction(to, tolerance: 3f, fly: true);
        }
        return c.Submit(leg);
    }

    private bool Arrive(BrainContext c)
    {
        if (_finishAfterReturn)
        {
            c.Log.Info($"Слив опыта закончен: опыт {c.World.Host.ExpPercent:0.0} % — фармлю дальше");
            Stop(c);
            return false;
        }

        c.Log.Info("Слив опыта: на месте — собираю мобов");
        StartPull(c);
        _phase = Phase.Pull;
        return Pull(c);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (_phase != Phase.Return || outcome.Status is ActionStatus.Confirmed or ActionStatus.Cancelled)
            return;

        switch (outcome.Action)
        {
            // Не взлетели (нет крыльев или полёт здесь запрещён) — идём пешком автопутём
            case FlyAction { Up: true }:
                c.Log.Warning("Слив опыта: не взлетел — иду на место фарма пешком");
                _walk = true;
                break;
            case MoveAction { Fly: true } when ++_fails > FlightFails:
                c.Log.Warning("Слив опыта: не долетел до места фарма — дальше пешком");
                _walk = true;
                break;
            case MoveAction { Fly: true }:
                _cruise += FailClimb;
                c.Log.Info($"Слив опыта: не долетел — поднимаюсь до {_cruise / 10:0.0}");
                break;
            case MoveAction:
                Stop(c);
                c.RequestStop("слив опыта: не дошёл до места фарма");
                break;
        }
    }

    // Слив не выходит — фармим дальше, попробуем позже
    private bool Abandon(BrainContext c, string why)
    {
        c.Log.Warning($"Слив опыта брошен: {why} — фармлю дальше, попробую через {RetryAfter.TotalMinutes:0} мин");
        _retryAt = c.Now + RetryAfter;
        Stop(c);
        return false;
    }

    private void Stop(BrainContext c)
    {
        DropShot(c);
        _phase = Phase.Off;
        _mob = 0;
        Status = null;
    }

    public void Reset()
    {
        _phase = Phase.Off;
        _off = null;
        _retryAt = DateTime.MinValue;
        _shotAction = null;
        _mob = 0;
        _noLoss = 0;
        Status = null;
    }
}

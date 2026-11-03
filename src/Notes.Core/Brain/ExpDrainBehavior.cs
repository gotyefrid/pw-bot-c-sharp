using System;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// План слива: где фармили (туда — в конце), где умирать со второй смерти (<see cref="DeathAtFarm"/> — там же, где фармили, и мобы
/// из фильтра фарма), от каких мобов (<see cref="Mobs"/>) и слили ли уже (<see cref="Enough"/>). <see cref="StartAtFarm"/> — слив
/// начался у фарма (первая смерть — там, мобы из фильтра фарма); нет — уже на точке смерти (прилетели туда при «Старт»).
/// </summary>
public sealed record DrainPlan(Spot Farm, Spot Death, bool DeathAtFarm, Habitat Mobs, Func<BrainContext, bool> Enough, bool StartAtFarm = true);

public enum DrainStepKind
{
    /// <summary>Сливаем: ход занят.</summary>
    Working,
    /// <summary>Одна попытка кончилась (умерли и вернулись): слив смотрит, хватит ли.</summary>
    AttemptDone,
    /// <summary>Сейчас не выходит — фармим, попробуем позже.</summary>
    GaveUp,
    /// <summary>Этим способом не выйдет — до перезапуска бота не пробуем.</summary>
    Off,
}

public sealed record DrainStep(DrainStepKind Kind, string? Why = null)
{
    public static readonly DrainStep Working = new(DrainStepKind.Working);
    public static readonly DrainStep AttemptDone = new(DrainStepKind.AttemptDone);
    public static DrainStep GaveUp(string why) => new(DrainStepKind.GaveUp, why);
    public static DrainStep Off(string why) => new(DrainStepKind.Off, why);
}

/// <summary>
/// Способ слить опыт (смертью — <see cref="DeathDrain"/>; потом — джином). Слив решает «когда начать» и «хватит ли», способ —
/// «как». Действия способа уходят от имени слива, итоги приходят через него.
/// </summary>
public interface IExpDrainMethod
{
    /// <summary>Для лога: «смертью».</summary>
    string Name { get; }

    /// <summary>Что делает — для окна; null — ничего.</summary>
    string? Status { get; }

    /// <summary>Почему этим способом на этом сервере не слить; null — можно.</summary>
    string? WhyNot(BrainContext c);

    void Begin(BrainContext c, DrainPlan plan);
    DrainStep Tick(BrainContext c);
    void OnOutcome(BrainContext c, ActionOutcome outcome);

    /// <summary>Слив кончился (слит, брошен, выключен).</summary>
    void End(BrainContext c);

    void Reset();
}

/// <summary>
/// Слив опыта (<see cref="Settings.ExpDrainSettings"/>): не качаться выше заданного уровня. На нём при опыте от порога — сливать
/// способом (<see cref="IExpDrainMethod"/>), пока опыт не станет не больше цели.
/// <list type="bullet">
/// <item>Стоит в наборе почти первым: пока слив идёт, ход всегда у него — банки, пет, бой, лут, копание и возврат не ходят и
/// ему не мешают. Начинает только без боя и лута и когда никто не посреди дела (<see cref="BrainContext.InMiddle"/>: банк).</item>
/// <item>Куда возвращаться после слива, задаёт режим (<c>home</c>: у фарма — центр фарма и как там стоять); нет точки — где стоим.
/// Слив начался у фарма в воздухе (фарм летающих мобов) — возвращается на ту же высоту, в воде — на ту же глубину; не у фарма
/// (на точке смерти) — как запомнена точка фарма.</item>
/// <item>Где умирать: точка из настроек (<see cref="Settings.ExpDrainSettings.DeathPoint"/>) или место фарма. От каких мобов
/// (<see cref="Settings.ExpDrainSettings.DeathMobs"/>) — заодно и как ждать смерть: летающих — в воздухе, водных — в воде.</item>
/// </list>
/// </summary>
public sealed class ExpDrainBehavior(IExpDrainMethod method, Func<BrainContext, Spot?> home) : IBehavior
{
    /// <summary>Ближе этого к центру фарма (или в радиусе фарма) — слив начался у фарма.</summary>
    public const float NearFarm = 30f;

    /// <summary>Брошенный слив пробуем снова через столько.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);

    private bool _active;
    private DrainPlan? _plan;
    private string? _off;
    private DateTime _retryAt = DateTime.MinValue;

    public string Name => "слив опыта";
    public string? Status { get; private set; }

    /// <summary>Слив идёт.</summary>
    public bool Active => _active;

    public bool Tick(BrainContext c)
    {
        Status = null;
        if (!_active)
            return c.Settings.ExpDrain.Enabled && TryStart(c);
        if (!c.Settings.ExpDrain.Enabled)
        {
            c.Log.Info("Слив опыта выключен — фармлю дальше");
            End(c);
            return false;
        }
        return Step(c);
    }

    private bool Step(BrainContext c)
    {
        var step = method.Tick(c);
        Status = method.Status is { } s ? "слив опыта: " + s : null;
        switch (step.Kind)
        {
            case DrainStepKind.Working:
                return true;
            case DrainStepKind.AttemptDone:
                return _plan!.Enough(c) ? Finish(c) : Step(c);
            case DrainStepKind.GaveUp:
                c.Log.Warning($"Слив опыта брошен: {step.Why} — фармлю дальше, попробую через {RetryAfter.TotalMinutes:0} мин");
                _retryAt = c.Now + RetryAfter;
                End(c);
                return false;
            default:
                _off = step.Why;
                c.Log.Warning($"Слив опыта: {step.Why} — выключаю слив до перезапуска бота");
                return Finish(c);
        }
    }

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
        if (settings.TargetPercent >= settings.StartPercent)
        {
            c.Say("drain-bounds", $"Слив опыта: «сливать до» ({settings.TargetPercent} %) не меньше «сливать от» ({settings.StartPercent} %) — не сливаю",
                LogLevel.Warning, 3600);
            return false;
        }
        if (WhyNot(c) is { } why)
        {
            c.Say("drain-unsupported", $"Слив опыта {method.Name} на этом сервере не работает: {why}", LogLevel.Warning, 3600);
            return false;
        }
        // Добить моба и собрать лут, дождаться копания, каста и банка
        if (c.Fight.Busy || c.InMiddle is not null || c.Runner.BodyBusy(c.World) is not null)
            return false;

        _plan = Plan(c);
        c.Fight.Abort();
        c.Log.Info($"Опыт {percent:0.0} % на {host.Level} ур. (порог {settings.StartPercent} %) — сливаю {method.Name} до {settings.TargetPercent} %, "
                   + $"от {MobsText(_plan.Mobs)} мобов, {(_plan.DeathAtFarm ? "на месте фарма" : $"в точке «{settings.DeathPoint}» (со второй смерти)")}");
        method.Begin(c, _plan);
        _active = true;
        return Step(c);
    }

    private DrainPlan Plan(BrainContext c)
    {
        var host = c.World.Host;
        var settings = c.Settings.ExpDrain;
        var center = home(c);
        var radius = Math.Max(c.Settings.Target.FarmRadius, NearFarm);
        var atFarm = center is not { } h || host.Position.HorizontalDistanceTo(h.Position) <= radius;
        Spot farm;
        if (atFarm)
        {
            // У фарма: как стоим сейчас (фармили летающих — в воздухе, на этой высоте)
            var end = host.Flying == true ? TripEnd.Air : host.InWater == true ? TripEnd.Water : TripEnd.Ground;
            var at = center?.Position ?? host.Position;
            farm = new Spot(end == TripEnd.Ground ? at : at with { Height = host.Position.Height }, end);
        }
        else
            farm = center!.Value;

        var mobs = Mobs(settings, farm.End);
        var mobsEnd = End(mobs);
        var point = settings.FindDeathPoint();
        if (point is null && settings.DeathPoint.Length > 0)
            c.Say("drain-no-point", $"Слив опыта: точки «{settings.DeathPoint}» нет среди сохранённых — умираю на месте фарма", LogLevel.Warning, 3600);
        var death = point is null ? new Spot(farm.Position, mobsEnd) : new Spot(point.Position, mobsEnd);
        return new DrainPlan(farm, death, point is null, mobs, Enough, atFarm);
    }

    private static Habitat Mobs(ExpDrainSettings settings, TripEnd farm) => settings.DeathMobs switch
    {
        DrainMobs.Ground => Habitat.Ground,
        DrainMobs.Air => Habitat.Air,
        DrainMobs.Water => Habitat.Water,
        _ => farm switch { TripEnd.Air => Habitat.Air, TripEnd.Water => Habitat.Water, _ => Habitat.Ground },
    };

    private static TripEnd End(Habitat mobs) => mobs switch { Habitat.Air => TripEnd.Air, Habitat.Water => TripEnd.Water, _ => TripEnd.Ground };

    /// <summary>
    /// Пора сливать по опыту и настройкам (а сервер и бой не смотрим): для «Старт» издалека — лететь сразу на точку смерти, а не
    /// на фарм.
    /// </summary>
    public static bool Due(BrainContext c)
    {
        var s = c.Settings.ExpDrain;
        return s.Enabled && c.World.Host.Level == s.MaxLevel && s.TargetPercent < s.StartPercent
               && c.World.Host.ExpPercent is { } percent && percent >= s.StartPercent;
    }

    /// <summary>Точка для смертей и как на ней ждать смерть (от каких мобов); null — точки нет, умирать на месте фарма.</summary>
    public static Spot? DeathSpot(BrainContext c)
    {
        var s = c.Settings.ExpDrain;
        return s.FindDeathPoint() is { } point ? new Spot(point.Position, End(Mobs(s, c.FarmSpot?.End ?? TripEnd.Ground))) : null;
    }

    // Слили (или слив выключили, пока летели из города) — на место фарма
    private static bool Enough(BrainContext c)
        => !c.Settings.ExpDrain.Enabled || c.World.Host.ExpPercent is not { } percent || percent <= c.Settings.ExpDrain.TargetPercent;

    private static string MobsText(Habitat mobs) => mobs switch
    {
        Habitat.Air => "летающих",
        Habitat.Water => "водных",
        _ => "земных",
    };

    private string? WhyNot(BrainContext c)
    {
        if (c.World.Host.ExpToLevel is null)
            return "не знаем, сколько опыта до уровня";
        if (c.World.Host.Flying is null)
            return "не знаем, летит ли персонаж";
        return method.WhyNot(c);
    }

    private bool Finish(BrainContext c)
    {
        c.Log.Info($"Слив опыта закончен: опыт {c.World.Host.ExpPercent:0.0} % — фармлю дальше");
        End(c);
        return false;
    }

    private void End(BrainContext c)
    {
        method.End(c);
        _active = false;
        Status = null;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => method.OnOutcome(c, outcome);

    public void Reset()
    {
        _active = false;
        _off = null;
        _retryAt = DateTime.MinValue;
        method.Reset();
        Status = null;
    }
}

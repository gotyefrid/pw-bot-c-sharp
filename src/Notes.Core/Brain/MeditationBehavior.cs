using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Медитация («Общее → Медитация»): когда нет боя и HP или MP (или оба — как настроено) по порогу — сесть медитировать; сидя —
/// держать ход, пока все включённые не поднимутся до своих порогов «вставать»; тогда встать. Где: выбрана точка медитации —
/// долететь до неё (<see cref="FlightTrip"/> на своей высоте) и сесть на землю; «Здесь» — в воздухе сесть на землю под собой,
/// на земле — на месте. Улетали или садились с полётника — после медитации обратно на фарм (<see cref="BrainContext.FarmSpot"/>,
/// как велит точка: на землю или зависнуть в воздухе). Напали или идёт бой — встаём, путь бросаем, ход — бою; после боя решаем
/// заново. Не долетели до точки — садимся «здесь». Не садимся в воде, во время каста и копания. Не сели — пауза, потом снова.
/// В наборе — после банок и пета, перед боем.
/// </summary>
public sealed class MeditationBehavior : IBehavior
{
    /// <summary>Не сели — столько ждём перед новой попыткой.</summary>
    public static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(10);

    /// <summary>Пора садиться, а перс кастует, — ждём конца каста не дольше этого.</summary>
    public static readonly TimeSpan CastWaitMax = TimeSpan.FromSeconds(5);

    private DateTime _nextTry = DateTime.MinValue;
    private DateTime? _castSince;
    private bool _toldNoCapability;
    private FlightTrip? _trip;
    // Путь к месту медитации (true) или обратно на фарм (false)
    private bool _toSeat;
    // Улетали с фарма или садились с полётника — после медитации обратно на фарм
    private bool _mustReturn;
    // Не долетели до точки — в этот раз садимся «здесь»
    private bool _here;

    public string Name => "медитация";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var s = c.Settings.Meditation;
        var w = c.World;
        var host = w.Host;
        if (!s.Enabled)
        {
            _trip = null;
            return false;
        }

        if (!c.Runner.Capabilities.Has(Capability.Meditate))
        {
            if (!_toldNoCapability)
            {
                _toldNoCapability = true;
                c.Log.Warning($"Медитация включена, но {c.Runner.Capabilities.WhyNot(Capability.Meditate)} — не медитирую");
            }

            return false;
        }

        var sitting = host.Sitting == true;
        if (TargetSelector.Aggressor(w) is not null || c.Fight.Busy)
        {
            _castSince = null;
            if (sitting && !c.Mine.Any(a => a is MeditateAction { Sit: false }))
            {
                c.Log.Info("Медитация: напали — встаю");
                Stand(c);
            }

            _trip = null;
            return false;
        }

        // Ждём ответа на «сесть» / «встать» — ход держим, чтобы никто не сдвинул перса
        if (c.Mine.Any(a => a is MeditateAction))
        {
            Status = "медитация: сажусь или встаю";
            return true;
        }

        if (sitting)
        {
            if (!Restored(host, s))
            {
                Status = $"медитирую ({Levels(host)})";
                return true;
            }

            c.Log.Info($"Медитация: {Levels(host)} — встаю");
            _here = false;
            Stand(c);
            Status = "медитация: встаю";
            return true;
        }

        if (_trip is not null)
            return Fly(c);

        var why = "";
        if (Restored(host, s) || !Wanted(host, s, out why))
        {
            _castSince = null;
            return _mustReturn && GoBack(c);
        }

        if (c.Now < _nextTry || host.InWater == true || host.Gather is { Active: true })
            return false;

        // Докастовывает (после убийства бой прерывает каст, игра снимает флаг не сразу) — ход держим, иначе бой возьмёт
        // следующего моба и пауза для медитации не наступит никогда; флаг завис — через CastWaitMax отдаём ход
        if (host.IsCasting)
        {
            _castSince ??= c.Now;
            if (c.Now - _castSince > CastWaitMax)
                return false;
            Status = "медитация: жду конца каста";
            return true;
        }

        _castSince = null;

        // Где сесть: точка медитации (если не долетели в этот раз — здесь); в воздухе над местом — сесть на землю
        var point = _here ? null : s.FindPoint();
        var seat = point is not null ? new Spot(point.Position, TripEnd.Ground) : new Spot(host.Position, TripEnd.Ground);
        if (point is not null && !FlightTrip.There(c, seat))
        {
            c.Log.Info($"Медитация: {why} — лечу к точке «{point.Name}» ({host.Position.HorizontalDistanceTo(point.Position):0} м)");
            return StartTrip(c, seat, toSeat: true);
        }

        if (host.Flying == true)
        {
            c.Log.Info($"Медитация: {why} — сажусь на землю");
            return StartTrip(c, seat, toSeat: true);
        }

        c.Log.Info($"Медитация: {why} — сажусь");
        _nextTry = c.Now + RetryPause;
        if (!c.Submit(new MeditateAction(true)))
            return false;

        Status = "сажусь медитировать";
        return true;
    }

    private bool StartTrip(BrainContext c, Spot to, bool toSeat)
    {
        _trip = new FlightTrip(to, c.Settings.Meditation.FlightHeight * 10);
        _toSeat = toSeat;
        if (toSeat)
            _mustReturn = true;
        return Fly(c);
    }

    private bool Fly(BrainContext c)
    {
        Status = _toSeat
            ? $"медитация: лечу на место, {c.World.Host.Position.HorizontalDistanceTo(_trip!.To):0} м"
            : $"медитация: лечу на фарм, {c.World.Host.Position.HorizontalDistanceTo(_trip!.To):0} м";
        var step = _trip.Step(c);
        if (step == TripStep.Going)
            return true;

        var failed = _trip.Failed;
        _trip = null;
        if (_toSeat)
        {
            if (step == TripStep.Failed)
            {
                c.Log.Warning($"Медитация: не добрался до места: {failed} — сажусь здесь");
                _here = true;
            }

            // Сесть — следующим шагом (там же проверки)
            Status = "медитация: на месте";
            return true;
        }

        _mustReturn = false;
        if (step == TripStep.Failed)
            c.Log.Warning($"Медитация: не добрался до фарма: {failed} — остаюсь здесь");
        else
            c.Log.Info("Медитация: вернулся на фарм");
        return false;
    }

    // После медитации — обратно на фарм, как велит его точка; уже там или центра нет — ничего
    private bool GoBack(BrainContext c)
    {
        if (c.FarmSpot is not { } spot || FlightTrip.There(c, spot))
        {
            _mustReturn = false;
            return false;
        }

        c.Log.Info("Медитация: лечу обратно на фарм");
        return StartTrip(c, spot, toSeat: false);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        _trip?.OnOutcome(c, outcome);
        if (outcome.Action is MeditateAction { Sit: true } && outcome.Status is not ActionStatus.Confirmed)
            c.Log.Warning($"Медитация: сесть не получилось ({outcome.Status}) — попробую через {RetryPause.TotalSeconds:0} с");
    }

    public void Reset()
    {
        _nextTry = DateTime.MinValue;
        _castSince = null;
        _trip = null;
        _mustReturn = false;
        _here = false;
        Status = null;
    }

    private static void Stand(BrainContext c) => c.Submit(new MeditateAction(false));

    /// <summary>Пора садиться: включённые условия по HP и MP (любое или оба) выполнены. MP без максимума в снимке — не учитываем.</summary>
    public static bool Wanted(HostState host, MeditationSettings s, out string why)
    {
        why = "";
        var hp = s.UseHp ? Matches(HpPercent(host), s.HpCompare, s.HpPercent) : (bool?)null;
        var mp = s.UseMp && MpPercent(host) is { } m ? Matches(m, s.MpCompare, s.MpPercent) : (bool?)null;
        if (hp is null && mp is null)
            return false;

        var wanted = s.RequireBoth ? hp != false && mp != false : hp == true || mp == true;
        if (wanted)
            why = Levels(host);
        return wanted;
    }

    /// <summary>Восстановились: все включённые (HP, MP) поднялись до своих порогов «вставать при».</summary>
    public static bool Restored(HostState host, MeditationSettings s)
    {
        var hpOk = !s.UseHp || HpPercent(host) >= s.StandHpPercent;
        var mpOk = !s.UseMp || MpPercent(host) is not { } m || m >= s.StandMpPercent;
        return hpOk && mpOk;
    }

    private static bool Matches(double value, MeditationCompare compare, int percent) => compare switch
    {
        MeditationCompare.AtMost => value <= percent,
        MeditationCompare.Above => value > percent,
        MeditationCompare.AtLeast => value >= percent,
        _ => value < percent,
    };

    private static double HpPercent(HostState host) => host.MaxHp <= 0 ? 100 : 100.0 * host.Hp / host.MaxHp;

    private static double? MpPercent(HostState host) => host.MaxMp is int max && max > 0 ? 100.0 * host.Mp / max : null;

    private static string Levels(HostState host)
        => $"HP {HpPercent(host):0} %" + (MpPercent(host) is { } mp ? $", MP {mp:0} %" : "");
}

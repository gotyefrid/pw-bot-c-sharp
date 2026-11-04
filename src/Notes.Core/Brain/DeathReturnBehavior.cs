using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>Одна смерть: опыт до неё и после воскрешения (null — не знаем).</summary>
public sealed record DeathRecord(int? ExpBefore, int? ExpAfter, double? PercentBefore, double? PercentAfter)
{
    /// <summary>Сколько опыта сняла смерть; null — не знаем.</summary>
    public int? Lost => ExpBefore is { } before && ExpAfter is { } after ? before - after : null;
}

/// <summary>
/// После смерти: встать в ближнем городе и вернуться на место (<see cref="FlightTrip"/>). Ждёт смерть тот, кто её и устраивает
/// (слив опыта: <see cref="Expect"/>). Смерть, которую никто не ждал: включено «встать и вернуться»
/// (<see cref="Settings.TargetSettings.ReviveAndReturn"/>) и режим сказал, куда (<c>home</c>: центр фарма), — встаём и летим туда на
/// высоте полёта к центру; иначе бот останавливается. Убили снова раньше, чем отфармили <see cref="QuietFarm"/> после возвращения
/// (или по пути), — перед вылетом стоим в городе, и с каждой такой смертью дольше на <see cref="PauseStep"/>: у места, видимо, тот,
/// кто убивает раз за разом, — пусть уйдёт. Отфармили <see cref="QuietFarm"/> без смерти — снова без паузы.
/// Перед вылетом на фарм после случайной смерти пет готовится в городе (<see cref="PetPrep"/>: воскресить, вылечить до полного,
/// отозвать) — на месте, где мобы нападают сами, остаётся только призвать. Готовим во время паузы, если она есть.
/// <list type="bullet">
/// <item>Стоит в наборе первым: пока встаём и летим обратно, ход у него, остальные не ходят — и сам слив тоже.</item>
/// <item>Куда лететь, спрашиваем у ждавшего уже после воскрешения: он знает, сколько сняла смерть (слив: ещё сливать — в точку
/// для смертей, слили — на место фарма).</item>
/// <item>Вернулись — ход свободен; кто ждал смерть, видит <see cref="Deaths"/> и <see cref="LastDeath"/> (сколько снято опыта).</item>
/// <item>Ожидание сняли (<see cref="Release"/>), пока встаём или летим, — всё равно долетаем: в городе бот не фармит.</item>
/// </list>
/// </summary>
public sealed class DeathReturnBehavior(Func<BrainContext, Spot?>? home = null) : IDeathBehavior
{
    /// <summary>Столько отфармили после возвращения без смерти — пауза перед вылетом сбрасывается.</summary>
    public static readonly TimeSpan QuietFarm = TimeSpan.FromMinutes(10);

    /// <summary>На столько дольше стоим в городе с каждой смертью подряд: 0, 5, 10, 15 мин…</summary>
    public static readonly TimeSpan PauseStep = TimeSpan.FromMinutes(5);

    /// <summary>После смерти до «Ближнего города» (окно смерти появляется не сразу).</summary>
    public static readonly TimeSpan ReviveDelay = TimeSpan.FromSeconds(2);

    /// <summary>Встали в городе — пауза до взлёта (мир вокруг подгружается).</summary>
    public static readonly TimeSpan AfterRevive = TimeSpan.FromSeconds(2.5);

    private static readonly TimeSpan ReviveRetry = TimeSpan.FromSeconds(5);
    public const int ReviveAttempts = 3;

    /// <summary>Дальше места — видимо, город другой или точка с другой локации: не летим.</summary>
    public const float MaxTrip = 3000f;

    private enum Phase { Idle, Dead, Revived, Return }

    private Phase _phase;
    private bool _expected;
    private Func<BrainContext, Spot>? _whereTo;
    private Spot _spot;
    private float _cruise;
    private FlightTrip? _trip;
    private PetPrep? _prep;
    // Случайные смерти подряд (без QuietFarm фарма между ними): от их числа — пауза в городе перед вылетом
    private int _streak;
    private TimeSpan _pause;
    // Летим на фарм после случайной смерти; когда долетели — от этого считаем QuietFarm
    private bool _accidentTrip;
    private DateTime? _backAt;

    private int? _expAlive;
    private double? _percentAlive;
    private int? _expBefore;
    private double? _percentBefore;
    private int _tries;
    private DateTime _reviveAt;
    private DateTime _since;

    public string Name => "после смерти";
    public string? Status { get; private set; }

    /// <summary>Встаём или летим обратно.</summary>
    public bool Busy => _phase != Phase.Idle;

    /// <summary>Сколько раз вставали (с запуска бота).</summary>
    public int Deaths { get; private set; }

    public DeathRecord? LastDeath { get; private set; }

    /// <summary>
    /// Следующая смерть — ожидаемая: встать в городе и лететь туда, куда скажет <paramref name="whereTo"/> (спросим после
    /// воскрешения, <see cref="LastDeath"/> уже известна), на высоте <paramref name="cruise"/> (как в памяти).
    /// </summary>
    public void Expect(Func<BrainContext, Spot> whereTo, float cruise)
    {
        _expected = true;
        _whereTo = whereTo;
        _cruise = cruise;
    }

    /// <summary>Смерть больше не ждём. Уже начатое (встаём, летим) доводим до конца.</summary>
    public void Release() => _expected = false;

    public bool Tick(BrainContext c)
    {
        Status = null;
        switch (_phase)
        {
            case Phase.Idle:
                _expAlive = c.World.Host.Exp;
                _percentAlive = c.World.Host.ExpPercent;
                return false;
            case Phase.Dead:
                return Revived(c);
            case Phase.Revived:
                return AfterRevival(c);
            default:
                return Return(c);
        }
    }

    public bool TickDead(BrainContext c)
    {
        if (_phase != Phase.Dead)
        {
            // Смерть никто не ждал: встать и вернуться на фарм, если так настроено. Нет — бот остановится; но если ещё в пути после
            // прошлой смерти — встаём и долетаем, как раньше
            _pause = TimeSpan.Zero;
            _accidentTrip = false;
            if (!_expected && !Accident(c) && _phase == Phase.Idle)
                return false;

            _phase = Phase.Dead;
            _trip = null;
            _prep = null;
            _expBefore = _expAlive;
            _percentBefore = _percentAlive;
            _tries = 0;
            _reviveAt = c.Now + ReviveDelay;
            c.Log.Info("Перс умер — встаю в ближнем городе");
        }

        Status = "встаю в ближнем городе";
        if (c.Mine.OfType<ReviveInTownAction>().Any() || c.Now < _reviveAt)
            return true;
        if (_tries >= ReviveAttempts)
        {
            c.Say("revive-town-failed", $"«Ближний город» не сработал {ReviveAttempts} раза", LogLevel.Warning);
            return false;
        }

        _tries++;
        _reviveAt = c.Now + ReviveRetry;
        c.Send(new ReviveInTownAction());
        return true;
    }

    // Случайная смерть: встаём, если включено и есть куда лететь. Убили снова вскоре после возвращения (или по пути) — пауза в городе
    private bool Accident(BrainContext c)
    {
        if (!c.Settings.Target.ReviveAndReturn || home?.Invoke(c) is null)
            return false;

        var soon = _phase != Phase.Idle || _backAt is { } back && c.Now - back < QuietFarm;
        _streak = soon ? _streak + 1 : 0;
        _pause = TimeSpan.FromTicks(PauseStep.Ticks * _streak);
        if (_pause > TimeSpan.Zero)
        {
            var after = _backAt is { } b && _phase == Phase.Idle ? $"через {(c.Now - b).TotalMinutes:0} мин после возвращения" : "по пути на фарм";
            c.Log.Warning($"Убили снова {after} — встану и постою в городе {_pause.TotalMinutes:0} мин, потом полечу на фарм");
        }

        _accidentTrip = true;
        _whereTo = c => home(c) ?? new Spot(c.World.Host.Position, TripEnd.Ground);
        _cruise = c.Settings.Target.TravelHeight * 10;
        return true;
    }

    // Первый живой снимок после смерти: сколько опыта сняли
    private bool Revived(BrainContext c)
    {
        var host = c.World.Host;
        LastDeath = new DeathRecord(_expBefore, host.Exp, _percentBefore, host.ExpPercent);
        Deaths++;
        c.Log.Info(LastDeath.Lost is { } lost
            ? $"Встал в городе: смерть сняла {lost} опыта ({_percentBefore:0.0} % → {host.ExpPercent:0.0} %)"
            : "Встал в городе, сколько опыта снято — не знаем");
        _phase = Phase.Revived;
        _since = c.Now;
        Status = "встал в городе";
        return true;
    }

    private bool AfterRevival(BrainContext c)
    {
        Status = "встал в городе";
        if (c.Now - _since < AfterRevive)
            return true;

        // После случайной смерти — на фарм: пета готовим здесь, пока ждём паузу
        if (_accidentTrip && _prep is null)
            _prep = new PetPrep(Habitat(_whereTo!(c).End), c.Now);
        var petReady = _prep?.Step(c) ?? true;
        var wait = AfterRevive + _pause - (c.Now - _since);
        if (wait > TimeSpan.Zero || !petReady)
        {
            if (_prep?.Status is { } pet)
                Status = pet;
            else if (_pause > TimeSpan.Zero)
                Status = $"жду в городе ещё {Math.Ceiling(wait.TotalMinutes):0} мин";
            return true;
        }

        _spot = _whereTo!(c);
        var distance = c.World.Host.Position.HorizontalDistanceTo(_spot.Position);
        if (distance > MaxTrip)
        {
            _phase = Phase.Idle;
            return c.RequestStop($"место в {distance:0} м от города — не лечу");
        }

        c.Log.Info($"Лечу на место, {distance:0} м");
        _trip = new FlightTrip(_spot, _cruise);
        _phase = Phase.Return;
        return Return(c);
    }

    private bool Return(BrainContext c)
    {
        var left = c.World.Host.Position.HorizontalDistanceTo(_spot.Position);
        Status = $"лечу на место, {left:0} м";
        switch (_trip!.Step(c))
        {
            case TripStep.Going:
                return true;
            case TripStep.Arrived:
                c.Log.Info("Вернулся на место");
                // После случайной смерти: отсюда считаем «отфармили спокойно»
                if (_accidentTrip)
                    _backAt = c.Now;
                _phase = Phase.Idle;
                _trip = null;
                Status = null;
                return false;
            default:
                _phase = Phase.Idle;
                return c.RequestStop($"после смерти {_trip.Failed}");
        }
    }

    // Среда места: какого пета готовить
    private static Habitat Habitat(TripEnd end) => end switch
    {
        TripEnd.Air => World.Habitat.Air,
        TripEnd.Water => World.Habitat.Water,
        _ => World.Habitat.Ground,
    };

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => _trip?.OnOutcome(c, outcome);

    public void Reset()
    {
        _phase = Phase.Idle;
        _expected = false;
        _whereTo = null;
        _trip = null;
        _prep = null;
        _streak = 0;
        _pause = TimeSpan.Zero;
        _accidentTrip = false;
        _backAt = null;
        _expAlive = _expBefore = null;
        _percentAlive = _percentBefore = null;
        Deaths = 0;
        LastDeath = null;
        Status = null;
    }
}

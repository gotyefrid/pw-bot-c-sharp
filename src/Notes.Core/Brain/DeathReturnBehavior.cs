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
/// После смерти: встать в ближнем городе и вернуться на место (<see cref="FlightTrip"/>). Смерть, которую никто не ждал, —
/// не наша: бот останавливается, как раньше. Ждёт смерть тот, кто её и устраивает (слив опыта: <see cref="Expect"/>); завтра
/// так же можно ждать любую смерть — «умер случайно — встать и фармить дальше».
/// <list type="bullet">
/// <item>Стоит в наборе первым: пока встаём и летим обратно, ход у него, остальные не ходят — и сам слив тоже.</item>
/// <item>Вернулись — ход свободен; кто ждал смерть, видит <see cref="Deaths"/> и <see cref="LastDeath"/> (сколько снято опыта).</item>
/// <item>Ожидание сняли (<see cref="Release"/>), пока встаём или летим, — всё равно долетаем: в городе бот не фармит.</item>
/// </list>
/// </summary>
public sealed class DeathReturnBehavior : IDeathBehavior
{
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
    private Position _home;
    private bool _stayInAir;
    private float _cruise;
    private FlightTrip? _trip;

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
    /// Следующая смерть — ожидаемая: встать в городе и вернуться в <paramref name="home"/> на высоте <paramref name="cruise"/>
    /// (как в памяти). <paramref name="stayInAir"/> — не садиться, а зависнуть на высоте точки.
    /// </summary>
    public void Expect(Position home, bool stayInAir, float cruise)
    {
        _expected = true;
        _home = home;
        _stayInAir = stayInAir;
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
        // Смерть никто не ждал, и мы не в пути после прошлой — бот остановится
        if (!_expected && _phase == Phase.Idle)
            return false;

        if (_phase != Phase.Dead)
        {
            _phase = Phase.Dead;
            _trip = null;
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

        var distance = c.World.Host.Position.HorizontalDistanceTo(_home);
        if (distance > MaxTrip)
        {
            _phase = Phase.Idle;
            return c.RequestStop($"место в {distance:0} м от города — не лечу");
        }

        c.Log.Info($"Лечу на место, {distance:0} м");
        _trip = new FlightTrip(_home, _cruise, _stayInAir);
        _phase = Phase.Return;
        return Return(c);
    }

    private bool Return(BrainContext c)
    {
        var left = c.World.Host.Position.HorizontalDistanceTo(_home);
        Status = $"лечу на место, {left:0} м";
        switch (_trip!.Step(c))
        {
            case TripStep.Going:
                return true;
            case TripStep.Arrived:
                c.Log.Info("Вернулся на место");
                _phase = Phase.Idle;
                _trip = null;
                Status = null;
                return false;
            default:
                _phase = Phase.Idle;
                return c.RequestStop($"после смерти {_trip.Failed}");
        }
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => _trip?.OnOutcome(c, outcome);

    public void Reset()
    {
        _phase = Phase.Idle;
        _expected = false;
        _trip = null;
        _expAlive = _expBefore = null;
        _percentAlive = _percentBefore = null;
        Deaths = 0;
        LastDeath = null;
        Status = null;
    }
}

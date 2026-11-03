using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Слив опыта смертью: отозвать пета, встать как велит вид мобов (летающих ждём в воздухе — взлететь, земных — сесть), собрать на
/// себя мобов (<see cref="MobPull"/>), стоять и ждать смерти. Встать в городе и долететь — дело <see cref="DeathReturnBehavior"/>:
/// смерть ему объявляется ожидаемой на всё время слива, а куда лететь, он спросит после воскрешения (<see cref="WhereTo"/>): ещё
/// сливать — в точку для смертей, слили — на место фарма. Вернулись — одна попытка кончилась (<see cref="DrainStep.AttemptDone"/>).
/// Амулет раз в 10 с восстанавливает всё HP, поэтому мобов несколько.
/// <list type="bullet">
/// <item>Мобы — только того вида, что в плане (где обитает — по справочнику игры). Вид неизвестен (поле не найдено для сервера) —
/// по высоте: на земле — только на нашей высоте (летающие на выстрел не подманиваются), в воздухе — любые.</item>
/// <item>На месте фарма — из фильтра фарма; в точке для смертей — любые: там фильтр фарма ни при чём.</item>
/// </list>
/// </summary>
public sealed class DeathDrain(DeathReturnBehavior afterDeath) : IExpDrainMethod
{
    /// <summary>Не убили за столько после сбора — собираем ещё.</summary>
    public static readonly TimeSpan DeathWait = TimeSpan.FromSeconds(60);

    /// <summary>Никто не бьёт столько — собираем заново, не дожидаясь <see cref="DeathWait"/> (мобы отстали).</summary>
    public static readonly TimeSpan CalmWait = TimeSpan.FromSeconds(10);

    /// <summary>Сколько раз собирать за одну жизнь, прежде чем бросить слив.</summary>
    public const int PullRounds = 3;

    /// <summary>Рядом нет мобов столько — бросаем слив.</summary>
    public static readonly TimeSpan NoMobsLimit = TimeSpan.FromSeconds(60);

    /// <summary>Столько смертей подряд не сняли опыт — слив выключаем до перезапуска бота.</summary>
    public const int NoLossDeaths = 2;

    /// <summary>Вид моба неизвестен, перс на земле — моб выше или ниже больше этого: летающий, на выстрел не подманивается.</summary>
    public const float GroundHeight = 5f;

    private enum Stage { RecallPet, Settle, Pull, WaitDeath }

    private Stage _stage;
    private DrainPlan _plan = null!;
    private bool _atFarm;
    private FlightTrip? _settle;
    private MobPull? _pull;
    private int _rounds;
    private int _deaths;
    private int _noLoss;
    private string? _off;
    private bool _back;
    private DateTime _since;
    private DateTime? _noMobsSince;
    private DateTime? _calmSince;

    public string Name => "смертью";
    public string? Status { get; private set; }

    public string? WhyNot(BrainContext c)
    {
        var caps = c.Runner.Capabilities;
        return caps.WhyNot(Capability.ReviveInTown) ?? caps.WhyNot(Capability.Move);
    }

    public void Begin(BrainContext c, DrainPlan plan)
    {
        _plan = plan;
        _atFarm = true;
        _noLoss = 0;
        _off = null;
        _deaths = afterDeath.Deaths;
        _back = false;
        afterDeath.Expect(WhereTo, c.Settings.ExpDrain.FlightHeight * 10);
        _stage = Stage.RecallPet;
    }

    // Встали в городе: смерть уже известна — решаем, сливать ли дальше, и отсюда — куда лететь
    private Spot WhereTo(BrainContext c)
    {
        if (afterDeath.LastDeath?.Lost is <= 0)
        {
            if (++_noLoss >= NoLossDeaths)
                _off = $"{NoLossDeaths} смерти подряд не сняли опыт";
        }
        else
            _noLoss = 0;

        var more = _off is null && !_plan.Enough(c);
        _atFarm = !more || _plan.DeathAtFarm;
        return more ? _plan.Death : _plan.Farm;
    }

    public DrainStep Tick(BrainContext c)
    {
        Status = null;
        if (afterDeath.Deaths != _deaths)
            return Died(c);

        switch (_stage)
        {
            case Stage.RecallPet:
                if (RecallPet(c))
                    return DrainStep.Working;
                _stage = Stage.Settle;
                _settle = new FlightTrip(new Spot(c.World.Host.Position, End(_plan.Mobs)), cruise: 0);
                return Settle(c);
            case Stage.Settle:
                return Settle(c);
            case Stage.Pull:
                return Pull(c);
            default:
                return WaitDeath(c);
        }
    }

    // Умерли и вернулись на место: одна попытка
    private DrainStep Died(BrainContext c)
    {
        _deaths = afterDeath.Deaths;
        _pull?.Drop(c);
        if (_off is { } off)
            return DrainStep.Off(off);

        // Высоту могли поменять в окне
        afterDeath.Expect(WhereTo, c.Settings.ExpDrain.FlightHeight * 10);
        _back = true;
        _rounds = 0;
        NewRound(c);
        return DrainStep.AttemptDone;
    }

    private bool RecallPet(BrainContext c)
    {
        Status = "отзываю пета";
        if (c.Mine.OfType<RecallPetAction>().Any())
            return true;
        if (c.World.Pet is { IsSummoned: true } && c.Runner.Capabilities.Has(Capability.RecallPet)
            && c.Send(new RecallPetAction()) == SubmitStatus.Sent)
        {
            c.Log.Info("Слив опыта: отзываю пета");
            return true;
        }
        return false;
    }

    // Перед первым сбором: летающих ждём в воздухе — взлететь, земных — сесть (на месте, никуда не летим)
    private DrainStep Settle(BrainContext c)
    {
        Status = "готовлюсь";
        if (_settle!.Step(c) == TripStep.Going)
            return DrainStep.Working;
        _settle = null;
        _rounds = 0;
        NewRound(c);
        return Pull(c);
    }

    private static TripEnd End(Habitat mobs) => mobs switch
    {
        Habitat.Air => TripEnd.Air,
        Habitat.Water => TripEnd.Water,
        _ => TripEnd.Ground,
    };

    private void NewRound(BrainContext c)
    {
        var air = _plan.Mobs == Habitat.Air;
        _pull = new MobPull(c.Settings.ExpDrain.Mobs, m => Suits(c, m), inAir: air);
        _stage = Stage.Pull;
        _since = c.Now;
        _noMobsSince = null;
    }

    private bool Suits(BrainContext c, NpcInfo mob)
    {
        if (_atFarm && !TargetSelector.IsAllowed(mob, c.Settings.Target))
            return false;
        if (mob.Habitat is not null)
            return mob.Lives(_plan.Mobs);
        return _plan.Mobs == Habitat.Air || Math.Abs(mob.Offset.Vertical) <= GroundHeight;
    }

    private DrainStep Pull(BrainContext c)
    {
        if (_back)
        {
            _back = false;
            c.Log.Info("Слив опыта: на месте — собираю мобов");
        }

        var step = _pull!.Step(c);
        Status = _pull.Status;
        switch (step)
        {
            case PullStep.Done:
                _stage = Stage.WaitDeath;
                _since = c.Now;
                _calmSince = null;
                c.Log.Info($"Слив опыта: собрал {_pull.Pulled} мобов — стою и жду смерти");
                return DrainStep.Working;
            case PullStep.NoMobs:
                _noMobsSince ??= c.Now;
                if (c.Now - _noMobsSince >= NoMobsLimit)
                    return DrainStep.GaveUp($"рядом ({MobPull.PullRadius:0} м) нет подходящих мобов {NoMobsLimit.TotalSeconds:0} с");
                Status = "жду мобов рядом";
                return DrainStep.Working;
            default:
                _noMobsSince = null;
                return DrainStep.Working;
        }
    }

    private DrainStep WaitDeath(BrainContext c)
    {
        var w = c.World;
        var hitting = w.Mobs.Count(m => w.TargetsUs(m) && m.TargetWid == w.Host.Wid);
        Status = $"жду смерти, бьют {hitting}";
        _calmSince = hitting > 0 ? null : _calmSince ?? c.Now;
        var calm = c.Now - _calmSince >= CalmWait;
        if (c.Now - _since < DeathWait && !calm)
            return DrainStep.Working;

        if (++_rounds >= PullRounds)
            return DrainStep.GaveUp($"не убили за {PullRounds} сбора");
        c.Log.Info(calm
            ? $"Слив опыта: никто не бьёт {CalmWait.TotalSeconds:0} с — собираю мобов заново"
            : $"Слив опыта: не убили за {DeathWait.TotalSeconds:0} с — собираю ещё мобов");
        NewRound(c);
        return DrainStep.Working;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome) => _settle?.OnOutcome(c, outcome);

    public void End(BrainContext c)
    {
        _pull?.Drop(c);
        _pull = null;
        _settle = null;
        afterDeath.Release();
        Status = null;
    }

    public void Reset()
    {
        _pull = null;
        _settle = null;
        _noLoss = 0;
        _off = null;
        _back = false;
        Status = null;
    }
}

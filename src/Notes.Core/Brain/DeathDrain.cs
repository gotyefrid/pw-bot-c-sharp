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
/// <item>Мобы — только того вида, что в плане (где обитает — по справочнику игры); «любые» — ближайшие, без разбора. Вид неизвестен
/// (поле не найдено для сервера) — по высоте: на земле — только на нашей высоте (летающие на выстрел не подманиваются), в воздухе — любые.</item>
/// <item>Список мобов фарма не смотрим ни на месте фарма, ни в точке для смертей: умереть можно от кого угодно.</item>
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
    private FlightTrip? _settle;
    private MobPull? _pull;
    private int _rounds;
    private int _deaths;
    private int _noLoss;
    private string? _off;
    private bool _back;
    // Куда летим из города — от этого высота (Cruise). Решает WhereTo
    private enum Leg { FirstToDeath, ToDeath, ToFarm }
    private Leg _leg;
    // Умираем на точке для смертей (встаём в её городе), а не на месте фарма (город у фарма) — к месту слива на «обычной» высоте
    private bool _diesAtDeathPoint;
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
        _noLoss = 0;
        _off = null;
        _deaths = afterDeath.Deaths;
        _back = false;
        // Слив начался на точке для смертей (прилетели туда при «Старт») — первой смерти у фарма не будет
        _diesAtDeathPoint = !plan.StartAtFarm && !plan.DeathAtFarm;
        afterDeath.Expect(WhereTo, Cruise);
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
        _leg = !more ? Leg.ToFarm : _diesAtDeathPoint ? Leg.ToDeath : Leg.FirstToDeath;
        // Летим умирать на точку для смертей — следующая смерть уже там (точки нет — снова у фарма)
        if (more)
            _diesAtDeathPoint = !_plan.DeathAtFarm;
        return more ? _plan.Death : _plan.Farm;
    }

    // Высоту спрашивают сразу после WhereTo и уже в городе: поменяли в окне посреди слива — берётся новая
    private float Cruise(BrainContext c)
    {
        var drain = c.Settings.ExpDrain;
        return _leg switch
        {
            Leg.FirstToDeath => drain.FirstFlightHeight,
            Leg.ToDeath => drain.FlightHeight,
            _ => drain.FarmFlightHeight,
        } * 10;
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
                _settle = new FlightTrip(new Spot(c.World.Host.Position, End(c, _plan.Mobs)), cruise: 0);
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

        afterDeath.Expect(WhereTo, Cruise);
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

    // Перед первым сбором: летающих ждём в воздухе — взлететь, земных — сесть (на месте, никуда не летим). Не сели — не сливаем:
    // из воздуха земных не собрать. После смерти так встаёт путь из города (точка — как велит вид мобов)
    private DrainStep Settle(BrainContext c)
    {
        Status = "готовлюсь";
        var step = _settle!.Step(c);
        if (step == TripStep.Going)
            return DrainStep.Working;
        if (step == TripStep.Failed)
            return DrainStep.GaveUp(_settle.Failed!);
        _settle = null;
        _rounds = 0;
        NewRound(c);
        return Pull(c);
    }

    // Любые мобы — ждём как стоим
    private static TripEnd End(BrainContext c, Habitat? mobs) => mobs switch
    {
        Habitat.Air => TripEnd.Air,
        Habitat.Water => TripEnd.Water,
        Habitat.Ground => TripEnd.Ground,
        _ => c.World.Host.Flying == true ? TripEnd.Air : c.World.Host.InWater == true ? TripEnd.Water : TripEnd.Ground,
    };

    private void NewRound(BrainContext c)
    {
        // Земных — ближних по горизонтали (по высоте рельеф), остальных — в пространстве
        _pull = new MobPull(c.Settings.ExpDrain.Mobs, Suits, inSpace: _plan.Mobs != Habitat.Ground);
        _stage = Stage.Pull;
        _since = c.Now;
        _noMobsSince = null;
    }

    private bool Suits(NpcInfo mob)
    {
        if (_plan.Mobs is not { } kind)
            return true;
        if (mob.Habitat is not null)
            return mob.Lives(kind);
        return kind == Habitat.Air || Math.Abs(mob.Offset.Vertical) <= GroundHeight;
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
                    return DrainStep.GaveUp($"рядом нет подходящих мобов {NoMobsLimit.TotalSeconds:0} с");
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
        var hitting = w.Mobs.Count(m => w.AttacksUs(m) && m.TargetWid == w.Host.Wid);
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

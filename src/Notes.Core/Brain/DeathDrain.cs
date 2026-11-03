using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;

namespace Notes.Core.Brain;

/// <summary>
/// Слив опыта смертью: отозвать пета, собрать на себя мобов (<see cref="MobPull"/>), стоять и ждать смерти. Встать в городе и
/// вернуться — дело <see cref="DeathReturnBehavior"/>: смерть ему объявляется ожидаемой на всё время слива. Вернулись — одна
/// попытка кончилась (<see cref="DrainStep.AttemptDone"/>), слив решает, хватит ли. Амулет раз в 10 с восстанавливает всё HP,
/// поэтому мобов несколько.
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

    private enum Stage { RecallPet, Pull, WaitDeath }

    private Stage _stage;
    private DrainPlace _place;
    private MobPull? _pull;
    private int _rounds;
    private int _deaths;
    private int _noLoss;
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

    public void Begin(BrainContext c, DrainPlace place)
    {
        _place = place;
        _noLoss = 0;
        _deaths = afterDeath.Deaths;
        _back = false;
        afterDeath.Expect(place.Home, place.InAir, c.Settings.ExpDrain.FlightHeight * 10);
        _stage = Stage.RecallPet;
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
                _rounds = 0;
                NewRound(c);
                return Pull(c);
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
        if (afterDeath.LastDeath?.Lost is <= 0)
        {
            if (++_noLoss >= NoLossDeaths)
                return DrainStep.Off($"{NoLossDeaths} смерти подряд не сняли опыт");
        }
        else
            _noLoss = 0;

        // Высоту могли поменять в окне
        afterDeath.Expect(_place.Home, _place.InAir, c.Settings.ExpDrain.FlightHeight * 10);
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

    private void NewRound(BrainContext c)
    {
        _pull = new MobPull(c.Settings.ExpDrain.Mobs, _place.InAir);
        _stage = Stage.Pull;
        _since = c.Now;
        _noMobsSince = null;
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
                    return DrainStep.GaveUp($"рядом ({MobPull.PullRadius:0} м) нет мобов {NoMobsLimit.TotalSeconds:0} с");
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

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
    }

    public void End(BrainContext c)
    {
        _pull?.Drop(c);
        _pull = null;
        afterDeath.Release();
        Status = null;
    }

    public void Reset()
    {
        _pull = null;
        _noLoss = 0;
        _back = false;
        Status = null;
    }
}

using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Медитация («Общее → Медитация»): сесть, когда нет боя и HP или MP (или оба — как настроено) по порогу; сидя — держать ход, пока
/// все включённые не восстановятся до «вставать при»; тогда встать. Напали или идёт бой — встаём и отдаём ход бою.
/// Не садимся в воздухе, в воде, во время каста и копания. Не получилось сесть — пауза, потом снова.
/// В наборе — после банок и пета, перед боем.
/// </summary>
public sealed class MeditationBehavior : IBehavior
{
    /// <summary>Не сели — столько ждём перед новой попыткой.</summary>
    public static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(10);

    private DateTime _nextTry = DateTime.MinValue;
    private bool _toldNoCapability;

    public string Name => "медитация";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var s = c.Settings.Meditation;
        var w = c.World;
        var host = w.Host;
        if (!s.Enabled)
            return false;
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
        var disturbed = TargetSelector.Aggressor(w) is not null || c.Fight.Busy;

        if (sitting)
        {
            if (disturbed)
            {
                c.Log.Info("Медитация: напали — встаю");
                Stand(c);
                return false;
            }

            if (Restored(host, s))
            {
                c.Log.Info($"Медитация: {Levels(host)} — встаю");
                Stand(c);
                return false;
            }

            Status = $"медитирую ({Levels(host)})";
            return true;
        }

        // Ждём ответа на «сесть» — ход держим, чтобы никто не сдвинул перса
        if (c.Mine.Any(a => a is MeditateAction { Sit: true }))
        {
            Status = "сажусь медитировать";
            return true;
        }

        if (disturbed || c.Now < _nextTry || host.Flying == true || host.InWater == true || host.IsCasting || host.Gather is not null
            || Restored(host, s) || !Wanted(host, s, out var why))
            return false;

        c.Log.Info($"Медитация: {why} — сажусь");
        _nextTry = c.Now + RetryPause;
        if (!c.Submit(new MeditateAction(true)))
            return false;

        Status = "сажусь медитировать";
        return true;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is MeditateAction { Sit: true } && outcome.Status is not ActionStatus.Confirmed)
            c.Log.Warning($"Медитация: сесть не получилось ({outcome.Status}) — попробую через {RetryPause.TotalSeconds:0} с");
    }

    public void Reset()
    {
        _nextTry = DateTime.MinValue;
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

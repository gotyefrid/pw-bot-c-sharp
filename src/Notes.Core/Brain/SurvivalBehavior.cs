using System.Collections.Concurrent;
using System.Collections.Generic;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Settings;

namespace Notes.Core.Brain;

/// <summary>
/// Бот сам переключил выбранную банку на «Авто» (её нет в сумке): окно ставит «Авто» и в своих настройках.
/// </summary>
public interface IPotionFallback
{
    /// <summary>Очередное переключение: вид банки и название, которое было выбрано; false — переключений нет.</summary>
    bool TryTakeSwitch(out PotionKind kind, out string name);
}

/// <summary>
/// Выжить: банка HP, когда HP ниже порога в % (и банка HP готова); банка MP, когда MP ниже порога в единицах — если такие
/// банки включены. Пьём выбранную банку, «Авто» — самую слабую подходящую по уровню (только банки с перезарядкой 10 с);
/// пока выпитая действует — такую же не пить. Выбранной нет в сумке — пишем в лог (раз за сессию) и переходим на «Авто»;
/// не хватает уровня — пишем (раз за сессию) и пьём «Авто», выбор не трогаем.
/// </summary>
public sealed class SurvivalBehavior : IBehavior, IPotionFallback
{
    private readonly PotionPolicy _policy = new();
    private readonly HashSet<string> _toldMissing = [];
    private readonly ConcurrentQueue<(PotionKind Kind, string Name)> _switches = new();

    public string Name => "банки";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var host = c.World.Host;
        var potions = c.Settings.Potions;

        if (potions.UseHp && host.HpPercent < potions.HpPercent
            && TryDrink(c, PotionKind.Hp, $"HP {host.HpPercent} % < {potions.HpPercent} %"))
            return true;

        return potions.UseMp && host.Mp < potions.MpBelow && TryDrink(c, PotionKind.Mp, $"MP {host.Mp} < {potions.MpBelow}");
    }

    private bool TryDrink(BrainContext c, PotionKind kind, string reason)
    {
        var potions = c.Settings.Potions;
        var chosen = kind == PotionKind.Hp ? potions.HpPotion : potions.MpPotion;
        var potion = _policy.Choose(c.World, kind, chosen, out var why, out var state);
        if (state.Missing)
            SwitchToAuto(c, potions, kind, chosen);
        else if (state.NeedsLevel > 0 && _toldMissing.Add($"{kind}|{chosen.ToLowerInvariant()}|ур"))
            c.Log.Warning($"Банке {kind} «{chosen}» нужен ур. {state.NeedsLevel} — пока пью «Авто», с этого уровня — её");

        if (potion is null)
        {
            if (why.StartsWith("нет банок"))
                c.Say("no-potion-" + kind, $"{reason}, но {why}", LogLevel.Warning);
            return false;
        }

        var action = new UseItemAction(potion, ItemUse.Potion, kind);
        if (c.Runner.IsPending(action.Key))
            return false;

        c.Log.Info($"{reason} — пью банку {potion.Name ?? "tid " + potion.Tid}");
        if (!c.Submit(action))
            return false;

        Status = $"пью банку {kind}";
        return true;
    }

    // Своя копия настроек — сразу «Авто»; окно узнает через IPotionFallback и поставит «Авто» у себя (и сохранит)
    private void SwitchToAuto(BrainContext c, PotionSettings potions, PotionKind kind, string chosen)
    {
        if (kind == PotionKind.Hp)
            potions.HpPotion = "";
        else
            potions.MpPotion = "";
        if (_toldMissing.Add(kind + "|" + chosen.ToLowerInvariant()))
            c.Log.Warning($"Банки {kind} «{chosen}» нет в сумке — переключаю на «Авто»");
        _switches.Enqueue((kind, chosen));
    }

    public bool TryTakeSwitch(out PotionKind kind, out string name)
    {
        var taken = _switches.TryDequeue(out var s);
        (kind, name) = (s.Kind, s.Name ?? "");
        return taken;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is UseItemAction { Potion: { } kind } drink && outcome.Status == ActionStatus.Confirmed)
            _policy.Drunk(drink.Item, kind, c.Now);
    }

    public void Reset() => Status = null;
}

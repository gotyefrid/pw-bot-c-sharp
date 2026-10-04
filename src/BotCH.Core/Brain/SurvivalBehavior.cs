using BotCH.Core.Actions;
using BotCH.Core.Logging;

namespace BotCH.Core.Brain;

/// <summary>
/// Выжить: банка HP, когда HP ниже порога в % (и банка HP готова); банка MP, когда MP ниже порога в единицах.
/// Самая слабая подходящая по уровню; пока выпитая действует — такую же не пить.
/// </summary>
public sealed class SurvivalBehavior : IBehavior
{
    private readonly PotionPolicy _policy = new();

    public string Name => "банки";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var host = c.World.Host;
        var potions = c.Settings.Potions;

        if (host.HpPercent < potions.HpPercent && TryDrink(c, PotionKind.Hp, $"HP {host.HpPercent} % < {potions.HpPercent} %"))
            return true;

        return host.Mp < potions.MpBelow && TryDrink(c, PotionKind.Mp, $"MP {host.Mp} < {potions.MpBelow}");
    }

    private bool TryDrink(BrainContext c, PotionKind kind, string reason)
    {
        var potion = _policy.Choose(c.World, kind, out var why);
        if (potion is null)
        {
            if (why.StartsWith("нет банок"))
                c.Say("no-potion-" + kind, $"{reason}, но {why}", LogLevel.Warning);
            return false;
        }

        var action = new UseItemAction(potion, ItemUse.Potion, kind);
        if (c.Runner.IsPending(action.Key))
            return false;

        c.Log.Info($"{reason} — пью банку tid {potion.Tid}");
        if (!c.Submit(action))
            return false;

        Status = $"пью банку {kind}";
        return true;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is UseItemAction { Potion: { } kind } drink && outcome.Status == ActionStatus.Confirmed)
            _policy.Drunk(drink.Item, kind, c.Now);
    }

    public void Reset() => Status = null;
}

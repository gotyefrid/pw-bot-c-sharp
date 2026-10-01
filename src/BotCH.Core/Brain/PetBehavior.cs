using BotCH.Core.Actions;
using BotCH.Core.Logging;

namespace BotCH.Core.Brain;

/// <summary>
/// Пет из выбранной клетки: не призван — призвать (мёртв — воскресить), HP ниже порога — вылечить, голоден — покормить.
/// Выключен в настройках или петов нет (не друид) — поведение молча пропускается.
/// </summary>
public sealed class PetBehavior : IBehavior
{
    private readonly PetFeeding _feeding = new();

    public string Name => "пет";
    public string? Status { get; private set; }

    public bool Tick(BrainContext c)
    {
        Status = null;
        var settings = c.Settings.Pet;
        var pet = c.World.Pet;
        if (!settings.Enabled || pet is null)
            return false;

        var inCage = pet.InCage(settings.Cage);
        if (inCage is null)
        {
            c.Say("pet-empty-cage", $"В клетке {settings.Cage} нет пета — пета пропускаю", seconds: 300);
            return false;
        }

        if (c.Runner.IsPending("пет"))
        {
            Status = "жду пета";
            return false;
        }

        if (!pet.IsSummoned)
        {
            if (inCage.IsAlive)
            {
                Status = "призываю пета";
                return c.Submit(new SummonPetAction(settings.Cage));
            }

            if (c.World.Skill(c.Skills.RevivePet) is not { IsReady: true })
            {
                c.Say("pet-revive-skill", "Пет мёртв, а скилл воскрешения не готов или не изучен", LogLevel.Warning, 60);
                return false;
            }

            Status = "воскрешаю пета";
            c.Log.Info("Пет мёртв — воскрешаю");
            return c.Submit(new RevivePetAction(settings.Cage, c.Skills.RevivePet));
        }

        if (pet.ActiveCage != settings.Cage)
        {
            c.Say("pet-other-cage", $"Призван пет из клетки {pet.ActiveCage}, а в настройках {settings.Cage} — не трогаю", seconds: 300);
            return false;
        }

        if (inCage.HpPercent < settings.HealPercent && c.World.Skill(c.Skills.HealPet) is { IsReady: true }
            && !c.Runner.IsPending($"скилл {c.Skills.HealPet}"))
        {
            Status = "лечу пета";
            c.Log.Info($"HP пета {inCage.HpPercent} % < {settings.HealPercent} % — лечу");
            return c.Submit(new SkillAction(c.Skills.HealPet, pet.ActiveWid, approach: true, "лечение пета"));
        }

        var food = _feeding.Choose(c.World, out var why);
        if (food is null)
        {
            if (why.StartsWith("нет"))
                c.Say("pet-food", $"Пет голоден, но {why}", LogLevel.Warning, 60);
            return false;
        }

        var feed = new UseItemAction(food, ItemUse.PetFood);
        if (c.Runner.IsPending(feed.Key))
            return false;

        Status = "кормлю пета";
        return c.Submit(feed);
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is UseItemAction { Use: ItemUse.PetFood } feed && outcome.Status != ActionStatus.Failed)
            c.Log.Info(_feeding.Report(feed.Item.Tid, outcome.Status == ActionStatus.Confirmed, c.Now));
    }

    public void Reset() => Status = null;
}

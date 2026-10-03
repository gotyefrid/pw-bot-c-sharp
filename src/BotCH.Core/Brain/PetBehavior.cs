using System;
using BotCH.Core.Actions;
using BotCH.Core.Logging;

namespace BotCH.Core.Brain;

/// <summary>
/// Пет из выбранной клетки: не призван — призвать (мёртв — воскресить), HP ниже порога — вылечить, голоден — покормить.
/// Выключен в настройках или петов нет (не друид) — поведение молча пропускается.
/// Лечение и воскрешение важнее всего: занятое тело освобождаем — бег/атаку забываем, каст или копание прерываем (как Esc).
/// </summary>
public sealed class PetBehavior : IBehavior
{
    // Лечение не пошло — жмём снова быстро. Подтверждение (перезарядка) в игре видно через 1,8–2,1 с, поэтому не 2 с
    private static readonly TimeSpan HealTimeout = TimeSpan.FromSeconds(2.5);

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
            if (FreeBody(c, c.Skills.RevivePet, "воскрешаю пета"))
                return true;
            if (c.Send(new RevivePetAction(settings.Cage, c.Skills.RevivePet)) != SubmitStatus.Sent)
                return false;
            c.Log.Info("Пет мёртв — воскрешаю");
            return true;
        }

        if (pet.ActiveCage != settings.Cage)
        {
            c.Say("pet-other-cage", $"Призван пет из клетки {pet.ActiveCage}, а в настройках {settings.Cage} — не трогаю", seconds: 300);
            return false;
        }

        if (inCage.HpPercent < settings.HealPercent && c.World.Skill(c.Skills.HealPet) is { IsReady: true })
        {
            Status = "лечу пета";
            if (FreeBody(c, c.Skills.HealPet, "лечу пета"))
                return true;

            // Тело всё ещё занято (прерывать нечем) — вылечим, как освободится; пока не мешаем остальным
            var heal = c.Send(new SkillAction(c.Skills.HealPet, pet.ActiveWid, approach: true, "лечение пета", HealTimeout));
            if (heal == SubmitStatus.Sent)
                c.Log.Info($"HP пета {inCage.HpPercent} % < {settings.HealPercent} % — лечу");
            if (heal is SubmitStatus.Sent or SubmitStatus.AlreadyPending)
            {
                Status = "лечу пета";
                return true;
            }
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

    /// <summary>
    /// Освободить тело ради пета скиллом <paramref name="skill"/>. true — ждём: кастуется этот же скилл (не сбиваем сами себя)
    /// или отправлена отмена чужого каста/копания. false — можно слать сразу (тело свободно, ждущее действие забыто)
    /// или прервать нечем (тогда пет ждёт, пока персонаж освободится).
    /// </summary>
    private static bool FreeBody(BrainContext c, int skill, string why)
    {
        var w = c.World;
        var body = c.Runner.BodyAction;
        if (body is RevivePetAction || body is SkillAction pending && pending.Skill == c.Skills.HealPet)
            return false;

        if (c.Runner.IsPending(new CancelAction(why).Key))
            return true;

        // Бег, подход, атака: игра заменит их скиллом — просто перестаём ждать
        if (body is not null)
        {
            c.Log.Info($"Пет важнее — бросаю «{body.Name}»: {why}");
            c.Runner.Cancel(body.Key, $"пет важнее: {why}", w);
        }

        var digging = w.Host.Gather is { Active: true };
        if (!w.Host.IsCasting && !digging)
            return false;

        // Кастуется этот же скилл — ждём его конца. Какой скилл, неизвестно (нет поля) — чужой ли он, не знаем: не сбиваем
        var casting = w.Host.CastingSkillId ?? 0;
        if (!digging && casting == 0)
            return false;
        if (!digging && casting == skill)
            return true;
        if (!c.Runner.Actions.CanCancel)
            return false;

        if (c.Send(new CancelAction(why)) == SubmitStatus.Sent)
            c.Log.Info($"Прерываю {(digging ? "копание" : "каст")}: {why}");
        return true;
    }

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is UseItemAction { Use: ItemUse.PetFood } feed && outcome.Status != ActionStatus.Failed)
            c.Log.Info(_feeding.Report(feed.Item.Tid, outcome.Status == ActionStatus.Confirmed, c.Now));
    }

    public void Reset() => Status = null;
}

using System;
using BotCH.Core.Actions;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;

namespace BotCH.Core.Brain;

/// <summary>
/// Пет для среды, где персонаж (<see cref="PetPicker"/>: в воздухе — летающий, на земле — наземный): не призван — призвать
/// (мёртв — воскресить), HP ниже порога — вылечить, голоден — покормить.
/// Выключен в настройках или петов нет (не друид) — поведение молча пропускается. «Только на время боя»
/// (<see cref="Settings.PetSettings.OnlyForFight"/>): без боя не призываем и не кормим, а через <see cref="CalmBeforeRecall"/>
/// после боя (вылечив) отзываем. Идёт ли бой — от <see cref="CombatBehavior"/>, как и у копания.
/// Лечение и воскрешение — срочные (<see cref="ActionPriority.Urgent"/>): что их пропускает вперёд, решает исполнитель.
/// </summary>
public sealed class PetBehavior(CombatBehavior combat) : IBehavior
{
    // Боя нет столько — отзываем (не сразу: моб мог отойти на шаг, лут ещё собирается)
    private static readonly TimeSpan CalmBeforeRecall = TimeSpan.FromSeconds(3);
    private DateTime? _calmSince;

    // Лечение не пошло — жмём снова быстро. Подтверждение (перезарядка) в игре видно через 1,8–2,1 с, поэтому не 2 с
    private static readonly TimeSpan HealTimeout = TimeSpan.FromSeconds(2.5);

    // Пету всё ещё нужно лечение, а оно перезаряжается: если ждать недолго — новую атаку не начинаем (лечение её тут же
    // сбило бы, каст и мана впустую), ждём лечение. Дольше — бьём как обычно
    private const int HealWaitMs = 3000;

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

        if (settings.OnlyForFight && c.Runner.Capabilities.Has(Capability.RecallPet))
        {
            // Бой — кто-то бьёт перса или пета, или бой/лут уже идёт
            if (TargetSelector.Aggressor(c.World) is not null || combat.State != CombatState.Search)
                _calmSince = null;
            else
            {
                _calmSince ??= c.Now;
                if (!pet.IsSummoned)
                {
                    Status = null;
                    return false;
                }
                if (c.Now - _calmSince >= CalmBeforeRecall && !NeedsHeal(c, pet) && FreeForRecall(c))
                {
                    Status = "отзываю пета";
                    if (c.Send(new RecallPetAction()) == SubmitStatus.Sent)
                        c.Log.Info($"Боя нет {CalmBeforeRecall.TotalSeconds:0} с — отзываю пета");
                    return false;
                }
            }
        }
        else if (settings.OnlyForFight)
            c.Say("pet-no-recall", "«Пет только на время боя»: на этом сервере отзыв пета не найден — пет остаётся призванным", LogLevel.Warning, 3600);

        var where = PetPicker.Where(c.World.Host);
        var inCage = PetPicker.Pick(pet, settings, where, out var problem);
        // Призван другой, но он живёт там, где мы сейчас (позвали руками), — он и есть наш пет
        if (pet.ActiveCage is int active && pet.InCage(active) is { } summoned && summoned.Lives(where))
            inCage = summoned;
        if (inCage is null)
        {
            c.Say($"pet-none-{where}", $"{char.ToUpper(problem![0])}{problem.Substring(1)} — пета пропускаю", seconds: 300);
            return false;
        }

        if (c.Runner.IsPending(ActionSlot.Pet))
        {
            Status = "жду пета";
            return false;
        }

        if (!pet.IsSummoned)
        {
            if (inCage.IsAlive)
            {
                Status = "призываю пета";
                if (inCage.Name is { } name)
                    c.Say($"pet-summon-{inCage.Cage}", $"Зову {name} (клетка {inCage.Cage}, {PetPicker.Text(where)})", seconds: 60);
                return c.Submit(new SummonPetAction(inCage.Cage));
            }

            if (c.World.Skill(c.Skills.RevivePet) is not { IsReady: true })
            {
                c.Say("pet-revive-skill", "Пет мёртв, а скилл воскрешения не готов или не изучен", LogLevel.Warning, 60);
                return false;
            }

            Status = "воскрешаю пета";
            if (c.Send(new RevivePetAction(inCage.Cage, c.Skills.RevivePet)) != SubmitStatus.Sent)
                return false;
            c.Log.Info("Пет мёртв — воскрешаю");
            return true;
        }

        if (pet.ActiveCage != inCage.Cage)
        {
            c.Say("pet-other-cage", $"Призван пет из клетки {pet.ActiveCage}, а нужен из клетки {inCage.Cage} — не трогаю", seconds: 300);
            return false;
        }

        if (inCage.HpPercent < settings.HealPercent && c.World.Skill(c.Skills.HealPet) is { IsReady: false, CooldownLeftMs: <= HealWaitMs })
        {
            Status = "жду перезарядку лечения пета";
            return true;
        }

        if (inCage.HpPercent < settings.HealPercent && c.World.Skill(c.Skills.HealPet) is { IsReady: true })
        {
            // Срочное: бег/атаку исполнитель забудет, чужой каст или копание прервёт. Занято (свой каст, прерывать нечем) —
            // вылечим, как освободится; пока не мешаем остальным
            var heal = c.Send(new SkillAction(c.Skills.HealPet, pet.ActiveWid, approach: true, "лечение пета", HealTimeout)
                { Priority = ActionPriority.Urgent });
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

    // Копание и каст не прерываем (отзыв важнее копания и сбил бы его); фоновый перелёт (обход, возврат в центр) — можно:
    // отзыв его заменит, а бег/полёт потом отправится снова
    private static bool FreeForRecall(BrainContext c)
        => c.Runner.BodyAction is MoveAction { Priority: ActionPriority.Background }
            ? !c.World.Host.IsCasting && c.World.Host.Gather is not { Active: true }
            : c.Runner.BodyBusy(c.World) is null;

    // Призванного пета нужно вылечить, и лечение есть (готово или скоро): отзываем потом
    private static bool NeedsHeal(BrainContext c, World.PetState pet)
        => pet.ActiveCage is int cage && pet.InCage(cage) is { } active && active.HpPercent < c.Settings.Pet.HealPercent
           && c.World.Skill(c.Skills.HealPet) is { CooldownLeftMs: <= HealWaitMs };

    public void OnOutcome(BrainContext c, ActionOutcome outcome)
    {
        if (outcome.Action is UseItemAction { Use: ItemUse.PetFood } feed && outcome.Status != ActionStatus.Failed)
            c.Log.Info(_feeding.Report(feed.Item.Tid, outcome.Status == ActionStatus.Confirmed, c.Now));
    }

    public void Reset()
    {
        Status = null;
        _calmSince = null;
    }
}

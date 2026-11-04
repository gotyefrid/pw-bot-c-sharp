using System;
using Notes.Core.Actions;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Пет перед вылетом из города на место фарма: мёртв — воскресить, призвать, вылечить до полного HP и отозвать. На месте (там бывают
/// мобы, что нападают сами) остаётся только призвать — без долгого воскрешения и лечения под ударами. Готовим того пета, что нужен
/// на месте (<see cref="PetPicker"/> для среды точки: в воздухе — летающий). Пет выключен или петов нет — готово сразу. Не вышло за
/// <see cref="MaxTime"/> (нет скилла, сервер не даёт) — в лог почему, и готово: летим как есть. Отзыва у сервера нет — летим с
/// призванным, но вылеченным петом.
/// Не поведение, а деталь поведения: шаги — из его Tick; действия уходят от его имени.
/// </summary>
public sealed class PetPrep(Habitat where, DateTime started)
{
    /// <summary>Дольше этого не готовим — летим как есть.</summary>
    public static readonly TimeSpan MaxTime = TimeSpan.FromMinutes(1);

    // Как в PetBehavior: подтверждение лечения в игре видно через 1,8–2,1 с
    private static readonly TimeSpan HealTimeout = TimeSpan.FromSeconds(2.5);

    private bool _done;

    /// <summary>Что делаем сейчас; null — готово или нечего делать.</summary>
    public string? Status { get; private set; }

    /// <summary>Шаг подготовки; true — готово, можно лететь.</summary>
    public bool Step(BrainContext c)
    {
        Status = null;
        if (_done)
            return true;
        var settings = c.Settings.Pet;
        var pet = c.World.Pet;
        if (!settings.Enabled || pet is null)
            return Done(c);
        if (c.Mine.Count > 0 || c.Runner.IsPending(ActionSlot.Pet))
        {
            Status = "готовлю пета";
            return false;
        }

        if (c.Now - started >= MaxTime)
            return Done(c, $"за {MaxTime.TotalSeconds:0} с пета подготовить не вышло — лечу как есть", LogLevel.Warning);

        var inCage = PetPicker.Pick(pet, settings, where, out var problem);
        if (inCage is null)
            return Done(c, $"{problem} — пета не готовлю");
        var here = PetPicker.Where(c.World.Host);
        // Где живёт, не знаем (поля не найдены) — пробуем, как и PetBehavior
        if (inCage.Habitat is not null && !inCage.CanSummon(here))
            return Done(c, $"пета для места ({PetPicker.Text(where)}) {PetPicker.Text(here)} не призвать — подготовлю на месте");

        if (!pet.IsSummoned)
        {
            if (inCage.HpPercent >= 100)
                return Done(c, inCage.Name is { } ready ? $"Пет {ready} готов — лечу" : "Пет готов — лечу", LogLevel.Info);
            if (inCage.IsAlive)
            {
                Status = "призываю пета, чтобы вылечить";
                c.Submit(new SummonPetAction(inCage.Cage));
                return false;
            }

            var revive = c.World.Skill(c.Skills.RevivePet);
            if (revive is null)
                return Done(c, "пет мёртв, а скилла воскрешения нет — воскрешу на месте", LogLevel.Warning);
            Status = "воскрешаю пета";
            if (!revive.IsReady)
                return false;
            if (c.Send(new RevivePetAction(inCage.Cage, c.Skills.RevivePet)) == SubmitStatus.Sent)
                c.Log.Info("Пет мёртв — воскрешаю в городе");
            return false;
        }

        if (pet.ActiveCage != inCage.Cage)
            return Done(c, $"призван пет из клетки {pet.ActiveCage}, а на месте нужен из клетки {inCage.Cage} — не трогаю");

        if (inCage.HpPercent < 100)
        {
            var heal = c.World.Skill(c.Skills.HealPet);
            if (heal is null)
                return Recall(c, "скилла лечения пета нет");
            Status = $"лечу пета, HP {inCage.HpPercent} %";
            if (heal.IsReady && c.Send(new SkillAction(c.Skills.HealPet, pet.ActiveWid, approach: true, "лечение пета", HealTimeout))
                    == SubmitStatus.Sent)
                c.Log.Info($"HP пета {inCage.HpPercent} % — лечу до полного перед вылетом");
            return false;
        }

        return Recall(c, null);
    }

    // Пет вылечен (или лечить нечем) — отозвать: на месте только призовём. Отзыва нет — летим с ним
    private bool Recall(BrainContext c, string? why)
    {
        if (c.Runner.Capabilities.WhyNot(Capability.RecallPet) is { } no)
            return Done(c, $"{(why is null ? "пет вылечен" : why)}, а отозвать нельзя ({no}) — лечу с петом", LogLevel.Info);
        Status = "отзываю пета";
        if (c.Send(new RecallPetAction()) == SubmitStatus.Sent)
            c.Log.Info(why is null ? "Пет вылечен — отзываю, на месте только призову" : $"{char.ToUpper(why[0])}{why.Substring(1)} — отзываю пета");
        return false;
    }

    private bool Done(BrainContext c, string? message = null, LogLevel level = LogLevel.Info)
    {
        _done = true;
        if (message is not null)
            c.Log.Log(level, $"{char.ToUpper(message[0])}{message.Substring(1)}");
        return true;
    }
}

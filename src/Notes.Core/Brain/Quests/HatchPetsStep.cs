using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Logging;

namespace Notes.Core.Brain.Quests;

/// <summary>
/// Вывести питомцев из всех яиц в сумке у NPC-дрессировщика, с которым идёт разговор (как окном «Инкубация питомца»): по яйцу
/// за раз. Яйцо после просьбы осталось в сумке — предупредить (клетки заняты?) и больше его не трогать.
/// </summary>
public sealed class HatchPetsStep : QuestStep
{
    private readonly HashSet<int> _asked = [];
    private DateTime _askedAt;

    public override string Title => "вывести питомцев из яиц";

    public override QuestStepState Tick(BrainContext c, int questId)
    {
        if (_asked.Count > 0 && c.Now - _askedAt < AnswerWait)
            return QuestStepState.Running;

        var eggs = c.World.Inventory.Where(i => i.IsPetEgg).ToList();
        if (eggs.FirstOrDefault(e => !_asked.Contains(e.Slot)) is { } egg)
        {
            c.Log.Info($"Задание {questId}: вывожу питомца из «{egg.Name ?? egg.Tid.ToString()}»");
            c.Send(new NpcHatchPetAction(egg.Slot, egg.Tid));
            _asked.Add(egg.Slot);
            _askedAt = c.Now;
            return QuestStepState.Running;
        }

        foreach (var left in eggs)
            c.Log.Warning($"Задание {questId}: питомец из «{left.Name ?? left.Tid.ToString()}» не вывелся (клетки заняты?)");
        return QuestStepState.Done;
    }
}

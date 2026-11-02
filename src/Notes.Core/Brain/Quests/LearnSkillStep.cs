using System;
using Notes.Core.Actions;
using Notes.Core.Logging;

namespace Notes.Core.Brain.Quests;

/// <summary>
/// Выучить скилл у NPC, с которым идёт разговор (как в его окне навыков). Скилл уже есть — пропустить. Учебник, если нужен, —
/// в сумке (обычно — награда задания); не выучился за <see cref="QuestStep.AnswerWait"/> — предупредить и идти дальше.
/// </summary>
public sealed class LearnSkillStep : QuestStep
{
    private DateTime? _asked;

    /// <summary>Номер скилла.</summary>
    public int Skill { get; set; }

    public override string? Problem => Skill > 0 ? null : "нет номера скилла (skill)";

    public override string Title => $"выучить скилл {Skill}";

    public override QuestStepState Tick(BrainContext c, int questId)
    {
        if (c.World.Skill(Skill) is not null)
            return QuestStepState.Done;

        if (_asked is null)
        {
            c.Log.Info($"Задание {questId}: учу скилл {Skill}");
            c.Send(new NpcLearnSkillAction(Skill));
            _asked = c.Now;
            return QuestStepState.Running;
        }

        if (c.Now - _asked < AnswerWait)
            return QuestStepState.Running;

        c.Log.Warning($"Задание {questId}: скилл {Skill} не выучился (нет учебника или мал уровень?)");
        return QuestStepState.Done;
    }
}

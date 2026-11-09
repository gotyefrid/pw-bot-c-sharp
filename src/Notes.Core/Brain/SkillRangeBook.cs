using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Дальность скиллов по наблюдению: клиент, получив скилл «как кнопкой» издалека, подбегает ровно на дальность и начинает каст —
/// расстояние до цели в начале каста и есть дальность (берём наибольшее). Готовым числом её в памяти нет: клиент считает её кодом
/// скилла (ElementSkill::GetPrayRange в elementskill.dll — своя функция у каждого скилла, от дальности атаки перса и прибавки),
/// а лишний вызов функций клиента рискован.
/// <para>
/// Нужна обходу препятствий: скилл бьёт сквозь стены и камни, если цель в радиусе, — обходить незачем.
/// </para>
/// </summary>
public sealed class SkillRangeBook
{
    // Меньше — не дальность, а случайность (моб вплотную); больше — не верим (телепорт цели, сбой чтения)
    private const float MinRange = 2f;
    private const float MaxRange = 60f;

    // Насколько выросла дальность, чтобы сказать об этом в лог, м
    private const float Notable = 0.5f;

    private readonly Dictionary<int, float> _ranges = [];
    private bool _wasCasting;

    /// <summary>Выученная дальность скилла, м; null — ещё не видели его каста.</summary>
    public float? Of(int skill) => _ranges.TryGetValue(skill, out var range) ? range : null;

    /// <summary>
    /// Снимок: начался каст — запомнить расстояние до цели. Цель лечения пета — пет (<paramref name="healPet"/>), остальных —
    /// цель перса. Вернёт текст для лога, если дальность заметно выросла.
    /// </summary>
    public string? Observe(WorldState w, int healPet)
    {
        var started = w.Host.IsCasting && !_wasCasting;
        _wasCasting = w.Host.IsCasting;
        if (!started || w.Host.CastingSkillId is not (> 0 and var skill))
            return null;

        var targetWid = skill == healPet ? w.Pet?.ActiveWid ?? 0 : w.Host.TargetWid;
        if (targetWid == 0 || w.Npcs.FirstOrDefault(n => n.Wid == targetWid) is not { } target)
            return null;

        var distance = target.Offset.Direct;
        if (distance is < MinRange or > MaxRange)
            return null;

        var known = Of(skill);
        if (known >= distance)
            return null;

        _ranges[skill] = distance;
        return known is null || distance - known >= Notable
            ? $"Дальность скилла {skill}: не меньше {distance:0.0} м (каст начался с этого расстояния)"
            : null;
    }
}

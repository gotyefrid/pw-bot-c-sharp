using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Notes.Core.Brain.Quests;

/// <summary>Как идёт шаг скрипта задания.</summary>
public enum QuestStepState
{
    /// <summary>Ещё делается (ждём ответа сервера) — тот же шаг в следующий тик.</summary>
    Running,

    /// <summary>Готово (или делать нечего) — к следующему шагу.</summary>
    Done,
}

/// <summary>
/// Шаг скрипта задания (<see cref="Profiles.QuestScript"/>): что-то сверх обычного «взять — сделать — сдать». Создаётся заново на
/// каждый запуск скрипта, поэтому может помнить своё (что уже просил, когда). Новый вид шага — класс-наследник и строка в
/// <see cref="QuestStepKinds"/>; поля шага — свойства класса, в профиле — <c>{ "kind": "LearnSkill", "skill": 330 }</c>.
/// </summary>
public abstract class QuestStep
{
    /// <summary>Сколько ждать ответа сервера на просьбу шага.</summary>
    protected static readonly TimeSpan AnswerWait = TimeSpan.FromSeconds(1.5);

    /// <summary>Что не так с полями шага (для лога при загрузке); null — всё в порядке.</summary>
    public virtual string? Problem => null;

    /// <summary>Что делает — для лога.</summary>
    public abstract string Title { get; }

    /// <summary>Сделать шаг или его часть; <paramref name="questId"/> — задание скрипта (для лога).</summary>
    public abstract QuestStepState Tick(BrainContext c, int questId);
}

/// <summary>Все виды шагов скриптов заданий: имя в профиле → класс.</summary>
public static class QuestStepKinds
{
    private static readonly Dictionary<string, Func<QuestStep>> Create = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LearnSkill"] = () => new LearnSkillStep(),
        ["HatchPets"] = () => new HatchPetsStep(),
    };

    public static IReadOnlyCollection<string> All => Create.Keys;

    /// <summary>Шаг из профиля; вид неизвестен или поля неверные — null и причина.</summary>
    public static QuestStep? Parse(JObject json, out string? problem)
    {
        var kind = (string?)json["kind"] ?? "";
        if (!Create.TryGetValue(kind, out var create))
        {
            problem = $"неизвестный шаг «{kind}» (знаю: {string.Join(", ", All)})";
            return null;
        }

        var step = create();
        try
        {
            JsonSerializer.CreateDefault().Populate(json.CreateReader(), step);
        }
        catch (JsonException e)
        {
            problem = $"шаг «{kind}»: {e.Message}";
            return null;
        }

        problem = step.Problem is { } p ? $"шаг «{kind}»: {p}" : null;
        return problem is null ? step : null;
    }

    /// <summary>Шаги скрипта по порядку; неверные пропускаются, причины — в <paramref name="problems"/>.</summary>
    public static List<QuestStep> ParseAll(IEnumerable<JObject> steps, List<string> problems)
        => steps.Select(json => (Step: Parse(json, out var problem), Problem: problem))
            .Where(s => { if (s.Problem is not null) problems.Add(s.Problem); return s.Step is not null; })
            .Select(s => s.Step!)
            .ToList();
}

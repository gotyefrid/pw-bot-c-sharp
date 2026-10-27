using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Brain;
using Newtonsoft.Json;

namespace BotCH.Core.Clicker;

/// <summary>
/// Шаг кликера: что сделать (<see cref="Action"/>) и при каких условиях (<see cref="Conditions"/>: должны выполниться все,
/// нет условий — всегда). Список шагов — сценарий, кликер выполняет его по кругу (<see cref="ClickerBehavior"/>).
/// </summary>
public sealed class ClickerStep
{
    private ConditionList _conditions = new();

    public ClickerAction Action { get; set; } = new PressKeyAction();

    /// <summary>Условия шага — должны выполниться все; нет условий — всегда. О правке внутри говорит сам список.</summary>
    public ConditionList Conditions { get => _conditions; set => _conditions = value ?? new ConditionList(); }

    /// <summary>Файл до списка условий: у шага было одно условие — оно первым в список.</summary>
    [JsonProperty("condition")]
    private ClickerCondition? OldCondition
    {
        set
        {
            if (value is not null)
                _conditions.Insert(0, value);
        }
    }

    /// <summary>Выполнены ли все условия: да — почему (в подробный лог), нет — null.</summary>
    public string? Check(BrainContext c) => ConditionGroup.AllHold(c, Conditions);

    /// <summary>Условия для окна: «цель есть И HP меньше 50 %», нет — «всегда».</summary>
    public string DescribeConditions() => ConditionGroup.Join(Conditions, "И", "всегда");

    /// <summary>Без пустых шагов; у шага без действия — «нажать клавишу»; значения — в пределы.</summary>
    public static List<ClickerStep> Clean(IEnumerable<ClickerStep?>? steps)
        => (steps ?? [])
            .Where(s => s is not null)
            .Select(s =>
            {
                s!.Action ??= new PressKeyAction();
                s.Action.Normalize();
                s.Conditions = ConditionList.Clean(s.Conditions);
                return s;
            })
            .ToList();
}

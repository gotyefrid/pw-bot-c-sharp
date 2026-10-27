using System.Collections.Generic;
using System.Linq;

namespace BotCH.Core.Clicker;

/// <summary>
/// Шаг кликера: что сделать (<see cref="Action"/>) и при каком условии (<see cref="Condition"/>). Список шагов — сценарий,
/// кликер выполняет его по кругу (<see cref="ClickerBehavior"/>).
/// </summary>
public sealed class ClickerStep
{
    public ClickerAction Action { get; set; } = new PressKeyAction();

    public ClickerCondition Condition { get; set; } = new AlwaysCondition();

    /// <summary>Без пустых шагов; у шага без действия — «нажать клавишу», без условия — «всегда»; значения — в пределы.</summary>
    public static List<ClickerStep> Clean(IEnumerable<ClickerStep?>? steps)
        => (steps ?? [])
            .Where(s => s is not null)
            .Select(s =>
            {
                s!.Action ??= new PressKeyAction();
                s.Condition ??= new AlwaysCondition();
                s.Action.Normalize();
                s.Condition.Normalize();
                return s;
            })
            .ToList();
}

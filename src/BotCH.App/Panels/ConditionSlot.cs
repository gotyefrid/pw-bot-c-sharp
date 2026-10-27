using System;
using System.Collections.Generic;
using BotCH.App.Mvvm;
using BotCH.Core.Clicker;

namespace BotCH.App.Panels;

/// <summary>
/// Окно одного условия кликера (<see cref="ConditionWindow"/>): его вид, поля и «НЕ». Сменили вид — в списке на его месте
/// новое условие с полями по умолчанию, «НЕ» остаётся.
/// </summary>
public sealed class ConditionSlot(ConditionList list, ClickerCondition condition, Func<IReadOnlyList<PickOption>>? lootOptions)
    : ObservableObject
{
    private ClickerCondition _condition = condition;

    public IReadOnlyList<ClickerKind> Kinds => ClickerKinds.Conditions.All;

    /// <summary>Варианты предметов — для условия «рядом лежит предмет».</summary>
    public Func<IReadOnlyList<PickOption>>? LootOptions { get; } = lootOptions;

    /// <summary>Условие — его поля показывает шаблон окна для его класса.</summary>
    public ClickerCondition Condition => _condition;

    public string Kind
    {
        get => _condition.Kind;
        set
        {
            var at = list.IndexOf(_condition);
            if (value is null || value == _condition.Kind || at < 0 || ClickerKinds.Conditions.Create(value) is not { } next)
                return;

            next.Not = _condition.Not;
            list[at] = next;
            _condition = next;
            OnPropertyChanged(nameof(Condition));
            OnPropertyChanged();
        }
    }
}

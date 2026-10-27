using System;
using System.Collections.Generic;
using BotCH.App.Mvvm;
using BotCH.Core.Clicker;

namespace BotCH.App.Panels;

/// <summary>
/// Окно одного условия кликера (<see cref="ConditionWindow"/>): его вид, поля и «НЕ». Сменили вид — в списке на его месте
/// новое условие с полями по умолчанию, «НЕ» остаётся.
/// </summary>
public sealed class ConditionSlot(ConditionList list, ClickerCondition condition, ClickerChoices? choices)
    : ObservableObject
{
    private ClickerCondition _condition = condition;

    private static readonly IReadOnlyList<PickOption> AllKinds = PickLists.Kinds(ClickerKinds.Conditions);

    /// <summary>Виды условий — для выбора с поиском (привязка окна видит только свойства экземпляра).</summary>
    public IReadOnlyList<PickOption> Kinds => AllKinds;

    /// <summary>Варианты для выбора в полях условия.</summary>
    public ClickerChoices? Choices { get; } = choices;

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

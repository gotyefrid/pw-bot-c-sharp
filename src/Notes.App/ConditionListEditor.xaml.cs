using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Notes.App.Panels;
using Notes.Core.Clicker;

namespace Notes.App;

/// <summary>
/// Список условий кликера — у шага или внутри группы «И»/«ИЛИ»: добавить, удалить (кнопкой или Delete), двойной щелчок —
/// окно условия (<see cref="ConditionWindow"/>), а в нём у группы — снова такой список. Правки идут прямо в список.
/// </summary>
public partial class ConditionListEditor : UserControl
{
    public static readonly DependencyProperty ConditionsProperty = DependencyProperty.Register(
        nameof(Conditions), typeof(ConditionList), typeof(ConditionListEditor), new PropertyMetadata(null, OnConditionsChanged));

    public static readonly DependencyProperty ChoicesProperty = DependencyProperty.Register(
        nameof(Choices), typeof(ClickerChoices), typeof(ConditionListEditor));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(ConditionListEditor), new PropertyMetadata(""));

    public ConditionListEditor() => InitializeComponent();

    public ConditionList? Conditions
    {
        get => (ConditionList?)GetValue(ConditionsProperty);
        set => SetValue(ConditionsProperty, value);
    }

    /// <summary>Варианты для выбора в окне условия (предметы…).</summary>
    public ClickerChoices? Choices
    {
        get => (ClickerChoices?)GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    /// <summary>Что написать, когда условий нет («шаг выполняется всегда»).</summary>
    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    private static void OnConditionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var editor = (ConditionListEditor)d;
        if (e.OldValue is ConditionList old)
            old.CollectionChanged -= editor.ListChanged;
        if (e.NewValue is ConditionList now)
            now.CollectionChanged += editor.ListChanged;
        editor.UpdateHint();
    }

    private void ListChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHint();

    private void UpdateHint() => EmptyHint.Visibility = Conditions is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;

    private void Add(object sender, RoutedEventArgs e)
    {
        if (Conditions is not { } conditions || ClickerKinds.Conditions.Create(ClickerKinds.Conditions.All[0].Id) is not { } condition)
            return;

        conditions.Add(condition);
        List.SelectedItem = condition;
        Open(condition);
    }

    // Выделение — на следующее условие: Delete можно жать подряд
    private void Remove(object sender, RoutedEventArgs e)
    {
        if (Conditions is not { } conditions || List.SelectedItem is not ClickerCondition condition)
            return;

        var at = conditions.IndexOf(condition);
        conditions.Remove(condition);
        if (conditions.Count > 0)
        {
            List.SelectedIndex = Math.Min(at, conditions.Count - 1);
            (List.ItemContainerGenerator.ContainerFromIndex(List.SelectedIndex) as ListBoxItem)?.Focus();
        }
    }

    private void ListKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete)
            return;

        e.Handled = true;
        Remove(sender, e);
    }

    // Двойной щелчок по строке (не по пустому месту) — окно условия
    private void EditCondition(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(List, (DependencyObject)e.OriginalSource) is ListBoxItem { Content: ClickerCondition condition })
            Open(condition);
    }

    private void Open(ClickerCondition condition)
    {
        if (Conditions is not { } conditions)
            return;

        new ConditionWindow
        {
            Owner = Window.GetWindow(this),
            DataContext = new ConditionSlot(conditions, condition, Choices),
        }.ShowDialog();
    }
}

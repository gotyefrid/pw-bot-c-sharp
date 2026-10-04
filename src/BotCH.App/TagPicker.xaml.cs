using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BotCH.App;

/// <summary>Вариант в выпадающем списке: что добавить и как показать.</summary>
public sealed record PickOption(string Name, string Text);

/// <summary>
/// Мультивыбор из списка (как select2): выбранное — «чипсы» с крестиком, «＋ Добавить» — список вариантов с поиском.
/// Свой текст ввести нельзя — только выбрать из того, что предлагает <see cref="Options"/>.
/// </summary>
public partial class TagPicker : UserControl
{
    public static readonly DependencyProperty SelectedProperty = DependencyProperty.Register(
        nameof(Selected), typeof(ObservableCollection<string>), typeof(TagPicker), new PropertyMetadata(null, OnSelectedChanged));

    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(Func<IReadOnlyList<PickOption>>), typeof(TagPicker));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(TagPicker), new PropertyMetadata(""));

    public static readonly DependencyProperty EmptyOptionsTextProperty = DependencyProperty.Register(
        nameof(EmptyOptionsText), typeof(string), typeof(TagPicker), new PropertyMetadata("Нечего выбрать"));

    private List<PickOption> _all = [];

    public TagPicker() => InitializeComponent();

    /// <summary>Выбранные названия.</summary>
    public ObservableCollection<string>? Selected
    {
        get => (ObservableCollection<string>?)GetValue(SelectedProperty);
        set => SetValue(SelectedProperty, value);
    }

    /// <summary>Откуда брать варианты (зовётся при каждом открытии списка).</summary>
    public Func<IReadOnlyList<PickOption>>? Options
    {
        get => (Func<IReadOnlyList<PickOption>>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    public string EmptyOptionsText
    {
        get => (string)GetValue(EmptyOptionsTextProperty);
        set => SetValue(EmptyOptionsTextProperty, value);
    }

    private static void OnSelectedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (TagPicker)d;
        if (e.OldValue is ObservableCollection<string> old)
            old.CollectionChanged -= picker.SelectionChanged;
        if (e.NewValue is ObservableCollection<string> now)
            now.CollectionChanged += picker.SelectionChanged;
        picker.UpdateHint();
    }

    private void SelectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHint();

    private void UpdateHint()
    {
        var any = Selected is { Count: > 0 };
        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearAll(object sender, RoutedEventArgs e) => Selected?.Clear();

    private void OpenDropDown(object sender, RoutedEventArgs e)
    {
        var chosen = new HashSet<string>(Selected ?? [], StringComparer.OrdinalIgnoreCase);
        _all = (Options?.Invoke() ?? []).Where(o => !chosen.Contains(o.Name)).ToList();
        Search.Text = "";
        Filter();
        DropDown.IsOpen = true;
        Dispatcher.BeginInvoke(new Action(() => Search.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter();
    }

    private void Filter()
    {
        var text = Search.Text.Trim();
        var shown = _all.Where(o => text.Length == 0 || o.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        OptionList.ItemsSource = shown;
        if (shown.Count > 0)
            OptionList.SelectedIndex = 0;
        NothingHint.Text = _all.Count == 0 ? EmptyOptionsText : shown.Count == 0 ? "Ничего не найдено" : "";
        NothingHint.Visibility = NothingHint.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Choose(OptionList.SelectedItem as PickOption);
                e.Handled = true;
                break;
            case Key.Down when OptionList.Items.Count > 0:
                OptionList.SelectedIndex = Math.Min(OptionList.SelectedIndex + 1, OptionList.Items.Count - 1);
                OptionList.ScrollIntoView(OptionList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when OptionList.Items.Count > 0:
                OptionList.SelectedIndex = Math.Max(OptionList.SelectedIndex - 1, 0);
                OptionList.ScrollIntoView(OptionList.SelectedItem);
                e.Handled = true;
                break;
            case Key.Escape:
                DropDown.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OptionsKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Choose(OptionList.SelectedItem as PickOption);
            e.Handled = true;
        }
    }

    private void OptionClicked(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is PickOption option)
            Choose(option);
    }

    private void Choose(PickOption? option)
    {
        if (option is null || Selected is null)
            return;

        if (!Selected.Contains(option.Name, StringComparer.OrdinalIgnoreCase))
            Selected.Add(option.Name);

        // Список остаётся открытым — можно выбрать несколько подряд
        _all.Remove(option);
        Search.Text = "";
        Filter();
        Search.Focus();
    }

    private void RemoveChip(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string name)
            Selected?.Remove(name);
    }
}

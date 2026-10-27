using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace BotCH.App;

/// <summary>
/// Выбор одного варианта с поиском: щелчок (или просто начать печатать) — список, сверху строка поиска, ищется по любой
/// части названия. Как <see cref="TagPicker"/>, только выбирается одно — <see cref="Value"/> (имя варианта).
/// </summary>
public partial class ChoicePicker : UserControl
{
    public static readonly DependencyProperty OptionsProperty = DependencyProperty.Register(
        nameof(Options), typeof(IReadOnlyList<PickOption>), typeof(ChoicePicker), new PropertyMetadata(null, OnShownChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(ChoicePicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnShownChanged));

    public ChoicePicker() => InitializeComponent();

    /// <summary>Варианты по порядку — как в списке.</summary>
    public IReadOnlyList<PickOption>? Options
    {
        get => (IReadOnlyList<PickOption>?)GetValue(OptionsProperty);
        set => SetValue(OptionsProperty, value);
    }

    /// <summary>Имя выбранного варианта (<see cref="PickOption.Name"/>).</summary>
    public string? Value
    {
        get => (string?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // Варианта с таким именем нет (вид из другой версии бота) — показать как есть
    private static void OnShownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var picker = (ChoicePicker)d;
        picker.Shown.Text = picker.Options?.FirstOrDefault(o => o.Name == picker.Value)?.Text ?? picker.Value ?? "";
    }

    private void BoxClicked(object sender, RoutedEventArgs e) => Open("");

    private void BoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.F4 || e.SystemKey == Key.Down)
        {
            Open("");
            e.Handled = true;
        }
    }

    // Начали печатать на закрытом списке — открыть его, напечатанное — в поиск
    private void BoxTextInput(object sender, TextCompositionEventArgs e)
    {
        if (e.Text.Length == 0 || char.IsControl(e.Text[0]))
            return;
        Open(e.Text);
        e.Handled = true;
    }

    private void Open(string search)
    {
        Search.Text = search;
        Search.CaretIndex = search.Length;
        Filter();
        DropDown.IsOpen = true;
        Dispatcher.BeginInvoke(new Action(() => Search.Focus()), DispatcherPriority.Input);
    }

    // Пока список открыт, щелчок по полю только закрывает его, а не открывает снова
    private void DropDownOpened(object? sender, EventArgs e) => Box.IsHitTestVisible = false;

    private void DropDownClosed(object? sender, EventArgs e) => Box.IsHitTestVisible = true;

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Filter();
    }

    // Пока поиск пуст — выделен выбранный сейчас вариант, иначе — первый найденный
    private void Filter()
    {
        var text = Search.Text.Trim();
        var shown = (Options ?? []).Where(o => text.Length == 0 || o.Text.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        OptionList.ItemsSource = shown;
        OptionList.SelectedItem = (text.Length == 0 ? shown.FirstOrDefault(o => o.Name == Value) : null) ?? shown.FirstOrDefault();
        if (OptionList.SelectedItem is not null)
            OptionList.ScrollIntoView(OptionList.SelectedItem);
        NothingHint.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
                Close();
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
        else if (e.Key == Key.Escape)
        {
            Close();
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
        if (option is null)
            return;
        Value = option.Name;
        Close();
    }

    private void Close()
    {
        DropDown.IsOpen = false;
        Box.Focus();
    }
}

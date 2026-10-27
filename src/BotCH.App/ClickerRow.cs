using BotCH.App.Mvvm;
using BotCH.Core.Clicker;

namespace BotCH.App;

/// <summary>Строка сценария кликера: номер, что сделать, при каких условиях, отметка «сейчас этот шаг».</summary>
public sealed class ClickerRow(int number, ClickerStep step) : ObservableObject
{
    private bool _current;

    public ClickerStep Step { get; } = step;

    public string Number { get; } = $"{number}.";

    public string What => Step.Action.Describe();

    public string When => Step.DescribeConditions();

    /// <summary>Бот сейчас на этом шаге.</summary>
    public bool Current { get => _current; set => SetProperty(ref _current, value); }

    /// <summary>Поменяли действие или условия шага.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(What));
        OnPropertyChanged(nameof(When));
    }
}

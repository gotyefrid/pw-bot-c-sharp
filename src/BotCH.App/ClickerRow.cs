using BotCH.App.Mvvm;
using BotCH.Core.Settings;

namespace BotCH.App;

/// <summary>Строка списка правил кликера: номер, клавиша, когда жать, как часто.</summary>
public sealed class ClickerRow(int number, ClickerRule rule) : ObservableObject
{
    public ClickerRule Rule { get; } = rule;

    public string Number { get; } = $"{number}.";

    public string KeyText => Rule.Key.Length > 0 ? Rule.Key : "не задана";

    public string When => Rule.DescribeCondition();

    public string RepeatText => $"раз в {Rule.RepeatSeconds:0.##} с";

    /// <summary>Поменяли что-то в правиле.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(KeyText));
        OnPropertyChanged(nameof(When));
        OnPropertyChanged(nameof(RepeatText));
    }
}

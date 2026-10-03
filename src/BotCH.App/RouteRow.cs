using BotCH.App.Mvvm;
using BotCH.Core.Settings;

namespace BotCH.App;

/// <summary>Строка маршрута обхода: номер, название, что копать, сколько до точки, отметка «сейчас идём сюда».</summary>
public sealed class RouteRow(int number, RoutePoint point) : ObservableObject
{
    private string _distanceText = "";
    private bool _current;

    public RoutePoint Point { get; } = point;

    public string Number { get; } = $"{number}.";

    public string Name => Point.Name;

    /// <summary>Что копать у точки — коротко.</summary>
    public string What => Point.Describe();

    /// <summary>Поменяли «что копать» у точки.</summary>
    public void Refresh() => OnPropertyChanged(nameof(What));

    public string DistanceText { get => _distanceText; private set => SetProperty(ref _distanceText, value); }

    /// <summary>Бот сейчас идёт к этой точке (или копает у неё).</summary>
    public bool Current { get => _current; private set => SetProperty(ref _current, value); }

    public void Update(float distance, bool current)
    {
        DistanceText = float.IsNaN(distance) ? "" : $"{distance:0} м";
        Current = current;
    }
}

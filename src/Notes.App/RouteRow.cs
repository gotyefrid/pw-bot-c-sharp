using Notes.App.Mvvm;
using Notes.Core.Settings;

namespace Notes.App;

/// <summary>Строка маршрута обхода: номер, где точка (воздух/земля), радиус, что копать, сколько до неё, отметка «сейчас идём сюда».</summary>
public sealed class RouteRow(int number, RoutePoint point) : ObservableObject
{
    private string _distanceText = "";
    private bool _current;

    public RoutePoint Point { get; } = point;

    public string Number { get; } = $"{number}.";

    /// <summary>Что копать у точки — коротко.</summary>
    public string What => Point.Describe();

    /// <summary>«воздух» или «земля» — как добираться до точки.</summary>
    public string Place => Point.Flying ? "воздух" : "земля";

    /// <summary>Сколько вокруг точки искать ресурсы.</summary>
    public string RadiusText => $"{Point.Radius} м";

    /// <summary>Поменяли поля точки.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(What));
        OnPropertyChanged(nameof(Place));
        OnPropertyChanged(nameof(RadiusText));
    }

    public string DistanceText { get => _distanceText; private set => SetProperty(ref _distanceText, value); }

    /// <summary>Бот сейчас идёт к этой точке (или копает у неё).</summary>
    public bool Current { get => _current; private set => SetProperty(ref _current, value); }

    public void Update(float distance, bool current)
    {
        DistanceText = float.IsNaN(distance) ? "" : $"{distance:0} м";
        Current = current;
    }
}

using System;
using BotCH.App.Mvvm;
using BotCH.Core.Resources;

namespace BotCH.App;

/// <summary>Строка списка точек ресурсов: название, сколько до неё, что с ресурсом.</summary>
public sealed class SpotRow(ResourceSpot spot) : ObservableObject
{
    private float _distance = float.NaN;
    private string _state = "";

    public ResourceSpot Spot { get; } = spot;

    public string Name => Spot.Name;

    public float Distance => _distance;

    public string DistanceText => float.IsNaN(_distance) ? "" : $"{_distance:0} м";

    public string State { get => _state; private set => SetProperty(ref _state, value); }

    public void Update(float distance, bool present, DateTime now)
    {
        if (float.IsNaN(_distance) || Math.Abs(distance - _distance) >= 1)
        {
            _distance = distance;
            OnPropertyChanged(nameof(Distance));
            OnPropertyChanged(nameof(DistanceText));
        }

        State = present ? "есть"
            : Spot.GoneAt is { } gone ? $"выкопан {Ago(now - gone)}"
            : Spot.LastSeen is { } seen ? $"видели {Ago(now - seen)}"
            : "ещё не видели";
    }

    private static string Ago(TimeSpan t)
        => t.TotalMinutes < 1 ? "только что"
            : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} мин назад"
            : t.TotalDays < 1 ? $"{(int)t.TotalHours} ч назад"
            : $"{(int)t.TotalDays} дн назад";
}

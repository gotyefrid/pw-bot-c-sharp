using System;
using System.Globalization;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Правка точки в окне (<see cref="PointWindow"/>): название и координаты как на миникарте игры (<see cref="MapCoords"/>). В точку
/// уходит только по «Сохранить» (<see cref="Save"/>), «Отмена» ничего не меняет.
/// </summary>
public sealed class PointDraft : ObservableObject
{
    private readonly BotSettings _settings;
    private readonly FarmPoint _point;
    private readonly Func<Position?> _here;
    private readonly string[] _reserved;
    private readonly string _shownX, _shownY, _shownHeight;
    private string _name, _x, _y, _height;
    private string _error = "";

    /// <param name="here">Где стоит персонаж; null — мир не читается.</param>
    /// <param name="reserved">Пункты списков вместо точки: так точку не назвать.</param>
    public PointDraft(BotSettings settings, FarmPoint point, Func<Position?> here, string[] reserved)
    {
        _settings = settings;
        _point = point;
        _here = here;
        _reserved = reserved;
        _name = point.Name;
        (_x, _y, _height) = Format(point.Position);
        (_shownX, _shownY, _shownHeight) = (_x, _y, _height);
        CurrentCommand = new RelayCommand(Current, () => _here() is not null);
    }

    /// <summary>Сохранили: точка уже изменена.</summary>
    public event Action? Saved;

    public string Title => $"Точка «{_point.Name}»";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string X { get => _x; set => SetProperty(ref _x, value); }
    public string Y { get => _y; set => SetProperty(ref _y, value); }
    public string Height { get => _height; set => SetProperty(ref _height, value); }

    /// <summary>Почему не сохранили; пусто — всё хорошо.</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>«Текущие»: подставить, где стоит персонаж.</summary>
    public ICommand CurrentCommand { get; }

    private void Current()
    {
        if (_here() is not { } p)
            return;
        (X, Y, Height) = Format(p);
        Error = "";
    }

    /// <summary>Проверить и записать в точку; false — что не так, в <see cref="Error"/>.</summary>
    public bool Save()
    {
        if (_settings.WhyNotName(_point, Name, _reserved) is { } why)
            return Fail(char.ToUpper(why[0]) + why.Substring(1));
        if (Parse(X) is not { } x || Parse(Y) is not { } y || Parse(Height) is not { } h)
            return Fail("Координаты — числа, как на миникарте: 394 447, высота 22");

        // Поле не трогали — координата прежняя до сантиметра (в поле она округлена)
        var old = _point.Position;
        var typed = new MapCoords(x, y, h).ToPosition();
        var p = new Position(X == _shownX ? old.X : typed.X, Height == _shownHeight ? old.Height : typed.Height, Y == _shownY ? old.Y : typed.Y);

        _settings.RenamePoint(_point, Name);
        (_point.X, _point.Y, _point.Height) = (p.X, p.Y, p.Height);
        Error = "";
        Saved?.Invoke();
        return true;
    }

    private bool Fail(string why)
    {
        Error = why;
        return false;
    }

    private static (string X, string Y, string Height) Format(Position p)
    {
        var map = MapCoords.From(p);
        return (Show(map.X), Show(map.Y), Show(map.Height));
    }

    private static string Show(double v) => v.ToString("0.#", CultureInfo.CurrentCulture);

    // Дробь — и через запятую, и через точку
    private static double? Parse(string text)
    {
        var t = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && !double.IsNaN(v) && !double.IsInfinity(v)
            ? v
            : null;
    }
}

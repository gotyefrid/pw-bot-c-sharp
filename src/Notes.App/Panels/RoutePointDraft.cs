using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Правка точки маршрута в окне (<see cref="RoutePointWindow"/>): название, координаты как на миникарте, «в воздухе», радиус
/// поиска и что копать. В точку уходит только по «Сохранить» (<see cref="Save"/>), «Отмена» ничего не меняет.
/// </summary>
public sealed class RoutePointDraft : ObservableObject
{
    private readonly List<RoutePoint> _points;
    private readonly RoutePoint _point;
    private readonly Func<HostState?> _here;
    private readonly string _shownX, _shownY, _shownHeight;
    private string _name, _x, _y, _height, _radius;
    private string _error = "";
    private bool _flying;
    private LootListMode _mode;

    /// <param name="points">Точки маршрута: название не должно повторяться в нём.</param>
    /// <param name="here">Персонаж сейчас; null — мир не читается.</param>
    /// <param name="options">Ресурсы для выбора: рядом и известные по блокноту.</param>
    public RoutePointDraft(List<RoutePoint> points, RoutePoint point, Func<HostState?> here, Func<IReadOnlyList<PickOption>> options)
    {
        _points = points;
        _point = point;
        _here = here;
        _name = point.Name;
        (_x, _y, _height) = PointText.Format(point.Position);
        (_shownX, _shownY, _shownHeight) = (_x, _y, _height);
        _radius = point.Radius.ToString(CultureInfo.CurrentCulture);
        _flying = point.Flying;
        _mode = point.ListMode;
        Options = options;
        foreach (var name in point.Resources)
            Resources.Add(name);
        CurrentCommand = new RelayCommand(Current, () => _here() is not null);
    }

    /// <summary>Сохранили: точка уже изменена.</summary>
    public event Action? Saved;

    public string Title => $"Точка «{_point.Name}»";

    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string X { get => _x; set => SetProperty(ref _x, value); }
    public string Y { get => _y; set => SetProperty(ref _y, value); }
    public string Height { get => _height; set => SetProperty(ref _height, value); }

    /// <summary>Радиус поиска ресурсов вокруг точки, м.</summary>
    public string Radius { get => _radius; set => SetProperty(ref _radius, value); }

    /// <summary>Точка в воздухе: бот взлетает (из воды сначала всплывает) и летит к ней на её высоте.</summary>
    public bool Flying { get => _flying; set => SetProperty(ref _flying, value); }

    /// <summary>Что копать: всё подряд / только из списка / всё, кроме списка.</summary>
    public LootListMode Mode { get => _mode; set => SetProperty(ref _mode, value); }

    public IReadOnlyList<LootListMode> Modes { get; } = [LootListMode.All, LootListMode.OnlyListed, LootListMode.ExceptListed];

    /// <summary>Названия ресурсов для «только» / «кроме».</summary>
    public ObservableCollection<string> Resources { get; } = new();

    /// <summary>Откуда брать варианты ресурсов (то же, что у выбора на вкладке).</summary>
    public Func<IReadOnlyList<PickOption>> Options { get; }

    /// <summary>Почему не сохранили; пусто — всё хорошо.</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }

    /// <summary>«Текущие»: подставить, где стоит персонаж.</summary>
    public ICommand CurrentCommand { get; }

    private void Current()
    {
        if (_here() is not { } host)
            return;
        (X, Y, Height) = PointText.Format(host.Position);
        // Где стоит сейчас: в воздухе — точка воздушная, иначе на земле (или в воде)
        Flying = host.Flying == true;
        Error = "";
    }

    /// <summary>Проверить и записать в точку; false — что не так, в <see cref="Error"/>.</summary>
    public bool Save()
    {
        var name = Name.Trim();
        if (name.Length == 0)
            return Fail("Название пустое");
        if (_points.Any(p => !ReferenceEquals(p, _point) && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            return Fail($"Точка «{name}» уже есть");
        if (PointText.Parse(X) is not { } x || PointText.Parse(Y) is not { } y || PointText.Parse(Height) is not { } h)
            return Fail("Координаты — числа, как на миникарте: 394 447, высота 22");
        if (!int.TryParse(Radius.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var radius) || radius is < 5 or > 500)
            return Fail("Радиус — целое число метров от 5 до 500");

        // Поле не трогали — координата прежняя до сантиметра (в поле она округлена)
        var old = _point.Position;
        var typed = new MapCoords(x, y, h).ToPosition();
        var p = new Position(X == _shownX ? old.X : typed.X, Height == _shownHeight ? old.Height : typed.Height, Y == _shownY ? old.Y : typed.Y);

        _point.Name = name;
        (_point.X, _point.Y, _point.Height) = (p.X, p.Y, p.Height);
        _point.Flying = Flying;
        _point.Radius = radius;
        _point.ListMode = Mode;
        _point.Resources = MobNameFilter.Clean(Resources);
        Error = "";
        Saved?.Invoke();
        return true;
    }

    private bool Fail(string why)
    {
        Error = why;
        return false;
    }
}

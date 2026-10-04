using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using BotCH.App.Mvvm;
using BotCH.Core.Logging;
using BotCH.Core.Resources;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.App.Panels;

/// <summary>
/// Вкладка «Ресы» — маршрут обхода (режим «Собирать ресурсы»): точки по порядку, что копать у каждой, опасные мобы.
/// Точки и списки — в настройках персонажа; о правке говорит модели окна (та сохраняет и отдаёт боту).
/// </summary>
public sealed class RoutePanel : ObservableObject
{
    private static readonly TimeSpan ShowEvery = TimeSpan.FromSeconds(1);

    private readonly Func<BotSettings> _settings;
    private readonly Func<WorldState?> _world;
    private readonly Func<int?> _current;
    private readonly SpotService _spots;
    private readonly ILogger _log;
    private readonly Action _edited;
    private RouteRow? _selectedRoutePoint;
    private DateTime _shown;
    private bool _syncing;

    /// <param name="settings">Настройки текущего персонажа (меняются при смене персонажа).</param>
    /// <param name="world">Последний снимок; null — мир не читается.</param>
    /// <param name="current">Номер точки, к которой бот идёт сейчас; null — бот не идёт по маршруту.</param>
    /// <param name="edited">Правка настроек из этой вкладки.</param>
    public RoutePanel(Func<BotSettings> settings, Func<WorldState?> world, Func<int?> current, SpotService spots, ILogger log, Action edited)
    {
        _settings = settings;
        _world = world;
        _current = current;
        _spots = spots;
        _log = log;
        _edited = edited;
        RouteNames.CollectionChanged += (_, _) => RouteNamesEdited();
        DangerNames.CollectionChanged += (_, _) => DangerNamesEdited();
        AddRoutePointCommand = new RelayCommand(AddRoutePoint, () => _world() is not null);
        RemoveRoutePointCommand = new RelayCommand(RemoveRoutePoint, () => SelectedRoutePoint is not null);
        RoutePointUpCommand = new RelayCommand(() => MoveRoutePoint(-1), () => SelectedRoutePoint is { } r && RouteRows.IndexOf(r) > 0);
        RoutePointDownCommand = new RelayCommand(() => MoveRoutePoint(1),
            () => SelectedRoutePoint is { } r && RouteRows.IndexOf(r) is var i && i >= 0 && i < RouteRows.Count - 1);
    }

    private BotSettings Settings => _settings();

    /// <summary>Точки обхода по порядку: после последней бот идёт к первой.</summary>
    public ObservableCollection<RouteRow> RouteRows { get; } = new();

    /// <summary>Список «что копать» выбранной точки (для её режима: только эти / всё, кроме этих).</summary>
    public ObservableCollection<string> RouteNames { get; } = new();

    /// <summary>Опасные мобы обхода по названию (боссы) — выбираются из мобов вокруг.</summary>
    public ObservableCollection<string> DangerNames { get; } = new();

    public ICommand AddRoutePointCommand { get; }
    public ICommand RemoveRoutePointCommand { get; }
    public ICommand RoutePointUpCommand { get; }
    public ICommand RoutePointDownCommand { get; }

    public RouteRow? SelectedRoutePoint
    {
        get => _selectedRoutePoint;
        set
        {
            if (!SetProperty(ref _selectedRoutePoint, value))
                return;
            LoadPointNames();
            OnPropertyChanged(nameof(HasSelectedRoutePoint));
            OnPropertyChanged(nameof(SelectedRouteMode));
            OnPropertyChanged(nameof(SelectedRouteTitle));
        }
    }

    /// <summary>С какой точки начинать обход (с 0): выбранная в списке, не выбрана — первая.</summary>
    public int StartIndex => SelectedRoutePoint is { } start ? Math.Max(0, RouteRows.IndexOf(start)) : 0;

    public bool HasSelectedRoutePoint => SelectedRoutePoint is not null;

    public string SelectedRouteTitle => SelectedRoutePoint is { } row ? $"Что копать у точки {RouteRows.IndexOf(row) + 1}" : "";

    /// <summary>Что копать у выбранной точки: всё подряд / только из списка / всё, кроме списка.</summary>
    public LootListMode SelectedRouteMode
    {
        get => SelectedRoutePoint?.Point.ListMode ?? LootListMode.All;
        set
        {
            if (SelectedRoutePoint is not { } row || row.Point.ListMode == value)
                return;
            row.Point.ListMode = value;
            row.Refresh();
            OnPropertyChanged();
            _edited();
        }
    }

    /// <summary>
    /// Варианты ресурсов (для точки обхода и для копания в фарме): рядом (и «нересурсы» — их можно копать, если назвать), потом
    /// известные по блокноту.
    /// </summary>
    public Func<IReadOnlyList<PickOption>> RouteOptions
        => () => PickLists.Options(
            _world() is { } w ? NearbyNames.Resources(w) : [],
            _spots.Spots.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => new NameCount(n, 0, 0)));

    /// <summary>Маршрут и опасные мобы в окне ← настройки персонажа (выбор сбрасывается, если точки больше нет).</summary>
    public void Load()
    {
        var selected = SelectedRoutePoint?.Point;
        RouteRows.Clear();
        var points = Settings.Route.Points;
        for (var i = 0; i < points.Count; i++)
            RouteRows.Add(new RouteRow(i + 1, points[i]));
        if (_world() is { } w)
        {
            foreach (var row in RouteRows)
                row.Update(w.Host.Position.HorizontalDistanceTo(row.Point.Position), false);
        }

        SelectedRoutePoint = RouteRows.FirstOrDefault(r => r.Point == selected);
        LoadPointNames();
        Sync(DangerNames, Settings.Route.DangerMobs);
    }

    /// <summary>Расстояния до точек и какая сейчас текущая — раз в секунду (или сразу).</summary>
    public void ShowDistances(WorldState w, bool force = false)
    {
        var now = DateTime.Now;
        if (!force && now - _shown < ShowEvery)
            return;
        _shown = now;

        var here = w.Host.Position;
        var current = _current();
        foreach (var row in RouteRows)
            row.Update(here.HorizontalDistanceTo(row.Point.Position), current == RouteRows.IndexOf(row));
    }

    private void AddRoutePoint()
    {
        if (_world() is not { } w)
            return;

        var points = Settings.Route.Points;
        var n = 1;
        while (points.Any(p => p.Name.Equals($"Точка {n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        var point = RoutePoint.At($"Точка {n}", w.Host.Position);
        points.Add(point);
        _log.Info($"Маршрут: точка {points.Count} {point.Position}{(w.Host.Flying == true ? " (в воздухе)" : "")}");
        Load();
        SelectedRoutePoint = RouteRows.LastOrDefault();
        _edited();
    }

    private void RemoveRoutePoint()
    {
        if (SelectedRoutePoint is not { } row)
            return;

        Settings.Route.Points.Remove(row.Point);
        _log.Info($"Маршрут: точка {RouteRows.IndexOf(row) + 1} удалена, осталось {Settings.Route.Points.Count}");
        Load();
        SelectedRoutePoint = null;
        _edited();
    }

    private void MoveRoutePoint(int step)
    {
        if (SelectedRoutePoint is not { } row)
            return;

        var points = Settings.Route.Points;
        var at = points.IndexOf(row.Point);
        var to = at + step;
        if (at < 0 || to < 0 || to >= points.Count)
            return;

        (points[at], points[to]) = (points[to], points[at]);
        Load();
        SelectedRoutePoint = RouteRows[to];
        OnPropertyChanged(nameof(SelectedRouteTitle));
        _edited();
    }

    /// <summary>Список «что копать» в окне ← выбранная точка.</summary>
    private void LoadPointNames() => Sync(RouteNames, SelectedRoutePoint?.Point.Resources ?? []);

    // Список в окне ← настройки (это не правка пользователя)
    private void Sync(ObservableCollection<string> target, IEnumerable<string> names)
    {
        _syncing = true;
        try
        {
            target.Clear();
            foreach (var name in names)
                target.Add(name);
        }
        finally
        {
            _syncing = false;
        }
    }

    private void RouteNamesEdited()
    {
        if (_syncing || SelectedRoutePoint is not { } row)
            return;

        row.Point.Resources = MobNameFilter.Clean(RouteNames);
        row.Refresh();
        _edited();
    }

    private void DangerNamesEdited()
    {
        if (_syncing)
            return;

        Settings.Route.DangerMobs = MobNameFilter.Clean(DangerNames);
        _edited();
    }
}

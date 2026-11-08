using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Threading;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Resources;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>
/// Вкладка «Ресы» — маршрут обхода (режим «Собирать ресурсы»): точки по порядку, что копать у каждой, опасные мобы.
/// Маршруты — общие у всех персонажей и копий бота (<see cref="RouteStore"/>): у персонажа только какой выбран, а его точки
/// окно кладёт в настройки (по ним идёт бот). Правка точек — сразу в общий файл; файл поменяла другая копия — точки
/// перечитываются. Опасные мобы и радиус — в настройках персонажа. О правке говорит модели окна (та сохраняет и отдаёт боту).
/// </summary>
public sealed class RoutePanel : ObservableObject, IDisposable
{
    // Файл поменяли — перечитать не сразу: запись другой копии — это несколько событий подряд
    private static readonly TimeSpan ReloadDelay = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan ShowEvery = TimeSpan.FromSeconds(1);

    private readonly Func<BotSettings> _settings;
    private readonly Func<WorldState?> _world;
    private readonly Func<int?> _current;
    private readonly SpotService _spots;
    private readonly ILogger _log;
    private readonly Action _edited;
    private readonly RouteStore _store;
    private readonly Func<string?> _owner;
    private readonly IDialogs _dialogs;
    private readonly FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _reload = new() { Interval = ReloadDelay };
    private RouteRow? _selectedRoutePoint;
    private DateTime _shown;
    private bool _syncing;

    /// <param name="settings">Настройки текущего персонажа (меняются при смене персонажа).</param>
    /// <param name="world">Последний снимок; null — мир не читается.</param>
    /// <param name="current">Номер точки, к которой бот идёт сейчас; null — бот не идёт по маршруту.</param>
    /// <param name="edited">Правка настроек из этой вкладки.</param>
    /// <param name="store">Общий файл маршрутов.</param>
    /// <param name="owner">Ник текущего персонажа (название маршрута из его старых точек); null — ещё неизвестен.</param>
    public RoutePanel(Func<BotSettings> settings, Func<WorldState?> world, Func<int?> current, SpotService spots, ILogger log, Action edited,
        RouteStore store, Func<string?> owner, IDialogs dialogs)
    {
        _settings = settings;
        _world = world;
        _current = current;
        _spots = spots;
        _log = log;
        _edited = edited;
        _store = store;
        _owner = owner;
        _dialogs = dialogs;
        AddRouteCommand = new RelayCommand(AddRoute);
        RenameRouteCommand = new RelayCommand(RenameRoute, () => SelectedRoute is not null);
        RemoveRouteCommand = new RelayCommand(RemoveRoute, () => SelectedRoute is not null);
        _reload.Tick += (_, _) =>
        {
            _reload.Stop();
            Reload();
        };
        if (Path.GetDirectoryName(store.Path) is { Length: > 0 } folder && Directory.Exists(folder))
        {
            _watcher = new FileSystemWatcher(folder, Path.GetFileName(store.Path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            };
            FileSystemEventHandler changed = (_, _) => UiThread.Run(() =>
            {
                _reload.Stop();
                _reload.Start();
            });
            _watcher.Changed += changed;
            _watcher.Created += changed;
            _watcher.Renamed += (o, e) => changed(o, e);
            _watcher.EnableRaisingEvents = true;
        }
        RouteNames.CollectionChanged += (_, _) => RouteNamesEdited();
        DangerNames.CollectionChanged += (_, _) => DangerNamesEdited();
        AddRoutePointCommand = new RelayCommand(AddRoutePoint, () => _world() is not null);
        RemoveRoutePointCommand = new RelayCommand(RemoveRoutePoint, () => SelectedRoutePoint is not null);
        RoutePointUpCommand = new RelayCommand(() => MoveRoutePoint(-1), () => SelectedRoutePoint is { } r && RouteRows.IndexOf(r) > 0);
        RoutePointDownCommand = new RelayCommand(() => MoveRoutePoint(1),
            () => SelectedRoutePoint is { } r && RouteRows.IndexOf(r) is var i && i >= 0 && i < RouteRows.Count - 1);
    }

    private BotSettings Settings => _settings();

    /// <summary>Сохранённые маршруты (общие у всех персонажей) по названию.</summary>
    public ObservableCollection<RouteChoice> Routes { get; } = new();

    /// <summary>Название маршрута: окну спросить у игрока (заголовок, текущее название); null — передумал.</summary>
    public Func<string, string, string?>? AskName { get; set; }

    public ICommand AddRouteCommand { get; }
    public ICommand RenameRouteCommand { get; }
    public ICommand RemoveRouteCommand { get; }

    // Пока список пересобирается — null, как у точек фарма (PointPicker): иначе окно потеряет выбор
    public RouteChoice? SelectedRoute
    {
        get => _syncing ? null : Routes.FirstOrDefault(r => r.Id == Settings.Route.RouteId);
        set
        {
            if (_syncing || value is null || value.Id == Settings.Route.RouteId)
                return;
            Choose(value.Id);
        }
    }

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
            PointsEdited();
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

    /// <summary>
    /// Маршрут и опасные мобы в окне ← настройки персонажа и общий файл маршрутов (выбор точки сбрасывается, если её больше нет).
    /// Старые точки персонажа становятся общим маршрутом; выбранный поменяли или удалили в другой копии — берём, как в файле.
    /// </summary>
    public void Load()
    {
        SyncWithStore();
        LoadRoutes();
        LoadRows();
        Sync(DangerNames, Settings.Route.DangerMobs);
    }

    // Файл маршрутов поменялся (мы сами или другая копия): перерисовать, только если что-то другое
    private void Reload()
    {
        var sync = SyncWithStore();
        var routes = _store.All();
        if (sync == RouteSync.Unchanged && routes.Select(r => (r.Id, r.Name, r.Points.Count)).SequenceEqual(Routes.Select(r => (r.Id, r.Name, r.Count))))
            return;
        LoadRoutes(routes);
        if (sync != RouteSync.Unchanged)
            LoadRows();
    }

    // Точки персонажа ← файл; поменялись — сохранить персонажа и отдать боту
    private RouteSync SyncWithStore()
    {
        var route = Settings.Route;
        var was = route.RouteId;
        var sync = _store.Sync(route, _owner() is { } nick ? $"Маршрут {nick}" : "Маршрут");
        if (_store.Problem is { } problem)
            _log.Warning(problem);
        switch (sync)
        {
            case RouteSync.Imported when was.Length == 0:
                _log.Info($"Точки маршрута персонажа перенесены в общие маршруты: «{_store.Find(route.RouteId)?.Name}»");
                break;
            case RouteSync.Imported:
                _log.Info($"Маршрута «{_store.Find(route.RouteId)?.Name}» не было в файле маршрутов — вернул его из точек персонажа");
                break;
            case RouteSync.Loaded:
                _log.Info($"Маршрут «{_store.Find(route.RouteId)?.Name}» изменён в другой копии бота — точки обновлены");
                break;
            case RouteSync.Deleted:
                _log.Info("Выбранный маршрут удалён — маршрут не выбран");
                break;
        }
        if (sync != RouteSync.Unchanged)
            _edited();
        return sync;
    }

    private void LoadRoutes(IReadOnlyList<SavedRoute>? routes = null)
    {
        _syncing = true;
        try
        {
            Routes.Clear();
            foreach (var route in routes ?? _store.All())
                Routes.Add(new RouteChoice(route.Id, route.Name, route.Points.Count));
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(nameof(SelectedRoute));
    }

    private void LoadRows()
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
    }

    public void Dispose()
    {
        _reload.Stop();
        _watcher?.Dispose();
    }

    // Выбрали маршрут: его точки — персонажу и боту
    private void Choose(string id)
    {
        var route = Settings.Route;
        var saved = _store.Find(id);
        route.RouteId = saved?.Id ?? "";
        route.Points = saved is null ? [] : RouteStore.Copy(saved.Points);
        _log.Info(saved is null ? "Маршрут не выбран" : $"Маршрут «{saved.Name}»: {saved.Points.Count} точек");
        SelectedRoutePoint = null;
        LoadRoutes();
        LoadRows();
        _edited();
    }

    private void AddRoute()
    {
        var route = _store.Create();
        _log.Info($"Новый маршрут «{route.Name}»");
        Choose(route.Id);
    }

    private void RenameRoute()
    {
        if (SelectedRoute is not { } route || AskName?.Invoke("Название маршрута", route.Name) is not { } name)
            return;
        if (name.Trim() == route.Name)
            return;
        if (!_store.Rename(route.Id, name))
        {
            _dialogs.Tell(name.Trim().Length == 0 ? "Название не может быть пустым" : $"Маршрут «{name.Trim()}» уже есть — выберите другое название");
            return;
        }
        _log.Info($"Маршрут «{route.Name}» переименован в «{name.Trim()}»");
        LoadRoutes();
    }

    private void RemoveRoute()
    {
        if (SelectedRoute is not { } route
            || !_dialogs.AskYesNo($"Удалить маршрут «{route.Name}» ({route.Count} точек)?\n\nОн общий — пропадёт у всех персонажей."))
            return;
        _store.Remove(route.Id);
        _log.Info($"Маршрут «{route.Name}» удалён");
        Choose("");
    }

    // Точки выбранного маршрута поменяли в окне — в общий файл (маршрут не выбран или его удалили в другой копии — новый)
    private void PointsEdited()
    {
        var route = Settings.Route;
        if (route.RouteId.Length == 0 || !_store.SavePoints(route.RouteId, route.Points))
        {
            var created = _store.Create();
            route.RouteId = created.Id;
            _store.SavePoints(created.Id, route.Points);
            _log.Info($"Точки записаны в новый маршрут «{created.Name}»");
        }
        LoadRoutes();
        _edited();
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
        PointsEdited();
        LoadRows();
        SelectedRoutePoint = RouteRows.LastOrDefault();
    }

    private void RemoveRoutePoint()
    {
        if (SelectedRoutePoint is not { } row)
            return;

        Settings.Route.Points.Remove(row.Point);
        _log.Info($"Маршрут: точка {RouteRows.IndexOf(row) + 1} удалена, осталось {Settings.Route.Points.Count}");
        PointsEdited();
        LoadRows();
        SelectedRoutePoint = null;
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
        PointsEdited();
        LoadRows();
        SelectedRoutePoint = RouteRows[to];
        OnPropertyChanged(nameof(SelectedRouteTitle));
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
        PointsEdited();
    }

    private void DangerNamesEdited()
    {
        if (_syncing)
            return;

        Settings.Route.DangerMobs = MobNameFilter.Clean(DangerNames);
        _edited();
    }
}

/// <summary>Маршрут в выпадающем списке: «Название (N точек)».</summary>
public sealed record RouteChoice(string Id, string Name, int Count)
{
    public override string ToString() => $"{Name} ({Count})";
}

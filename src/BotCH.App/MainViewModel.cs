using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BotCH.App.Mvvm;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Calls;
using BotCH.Core.Clients;
using BotCH.Core.GameFiles;
using BotCH.Core.Logging;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.Resources;
using BotCH.Core.Session;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.App;

/// <summary>
/// Модель главного окна. Окно только показывает её свойства и вызывает команды.
/// Снимок мира читается в фоне (<see cref="WorldMonitor"/>) и переносится в поток окна.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxLogLines = 500;

    private readonly ProfileCatalog _catalog = ProfileCatalog.Default();

    // Общие настройки (settings.json: подключение + шаблон) и текущего персонажа (characters\Ник.json)
    private readonly SettingsService _config;
    private readonly ILogger _log;
    private readonly ILogger _connectionLog;
    private readonly Logger _logger;

    // Подключение к клиенту: снимки, unfreeze и бот (он принадлежит подключению — окно только просит запустить и остановить)
    private ClientConnection? _connection;
    private ServerProfile _profile;
    private bool _refreshing;

    // Точки ресурсов — общие на все серверы, персонажей и копии бота (%AppData%\BotCH\resources.json), копятся в любом режиме
    private static readonly TimeSpan SpotsShowEvery = TimeSpan.FromSeconds(1);
    private readonly SpotService _spots;
    private DateTime _spotsShown;

    public MainViewModel(string appDirectory)
    {
        var ring = new RingBufferSink(MaxLogLines);
        var logger = new Logger().AddSink(ring).AddSink(new DailyFileSink(Path.Combine(appDirectory, "logs")));
        ring.Added += entry => OnUi(() => AddLog(entry));
        _logger = logger;
        _log = logger.For("окно");
        _connectionLog = logger.For("подключение");

        _config = new SettingsService(appDirectory, _log);

        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BotCH");
        _spots = new SpotService(shared, logger.For("ресурсы"));
        _spots.ImportOld(Path.Combine(appDirectory, "resources.json"));

        Servers = _catalog.Ids.Select(id => _catalog.Load(id)).ToList();
        _profile = Servers.FirstOrDefault(s => s.Id == _config.App.Connection.ServerId) ?? Servers.First();

        LoadNameLists();
        LoadFarmCenters();
        LoadRoute();
        MobNames.CollectionChanged += (_, _) => NameListsEdited();
        RouteNames.CollectionChanged += (_, _) => RouteNamesEdited();
        LootNames.CollectionChanged += (_, _) => NameListsEdited();
        FarmResourceNames.CollectionChanged += (_, _) => NameListsEdited();
        DangerNames.CollectionChanged += (_, _) => NameListsEdited();

        RefreshCommand = new RelayCommand(RefreshClients);
        StartCommand = new RelayCommand(Start, () => IsConnected && !IsRunning && !IsStopping);
        StopCommand = new RelayCommand(Stop, () => IsRunning);

        ClearLogCommand = new RelayCommand(Log.Clear);
        AddFarmPointCommand = new RelayCommand(AddFarmPoint, () => _lastWorld is not null);
        RemoveFarmPointCommand = new RelayCommand(RemoveFarmPoint, () => HasFarmPoint);
        AddRoutePointCommand = new RelayCommand(AddRoutePoint, () => _lastWorld is not null);
        RemoveRoutePointCommand = new RelayCommand(RemoveRoutePoint, () => SelectedRoutePoint is not null);
        RoutePointUpCommand = new RelayCommand(() => MoveRoutePoint(-1), () => SelectedRoutePoint is { } r && RouteRows.IndexOf(r) > 0);
        RoutePointDownCommand = new RelayCommand(() => MoveRoutePoint(1),
            () => SelectedRoutePoint is { } r && RouteRows.IndexOf(r) is var i && i >= 0 && i < RouteRows.Count - 1);

        _log.Info("BotCH запущен");
        RefreshClients();
    }

    // ── Подключение ──────────────────────────────────────────────────────────

    public IReadOnlyList<ServerProfile> Servers { get; }

    public ServerProfile SelectedServer
    {
        get => _profile;
        set
        {
            if (value is null || !SetProperty(ref _profile, value))
                return;

            _config.App.Connection.ServerId = value.Id;
            SaveSettings();
            OnPropertyChanged(nameof(CanChoosePath));
            OnPropertyChanged(nameof(CanRecallPet));
            // Тот же клиент, но читать его теперь по другим смещениям — переподключаемся всегда
            RefreshClients(reconnect: true);
        }
    }

    public ObservableCollection<GameClient> Clients { get; } = new();

    private GameClient? _selectedClient;

    public GameClient? SelectedClient
    {
        get => _selectedClient;
        set
        {
            var previous = _selectedClient?.Pid;
            if (!SetProperty(ref _selectedClient, value) || _refreshing || value?.Pid == previous)
                return;

            Connect(value);
        }
    }

    public bool RenameWindows
    {
        get => _config.App.Connection.RenameWindows;
        set
        {
            if (_config.App.Connection.RenameWindows == value)
                return;

            _config.App.Connection.RenameWindows = value;
            OnPropertyChanged();
            SaveSettings();
            if (value)
                RenameAll();
        }
    }

    public bool Unfreeze
    {
        get => _config.App.Connection.Unfreeze;
        set
        {
            if (_config.App.Connection.Unfreeze == value)
                return;

            _config.App.Connection.Unfreeze = value;
            OnPropertyChanged();
            SaveSettings();
            ApplyUnfreeze();
        }
    }

    private bool _isConnected;

    public bool IsConnected
    {
        get => _isConnected;
        private set => SetProperty(ref _isConnected, value);
    }

    private string _connectionText = "Нет подключения";

    public string ConnectionText
    {
        get => _connectionText;
        private set => SetProperty(ref _connectionText, value);
    }

    // ── Состояние ────────────────────────────────────────────────────────────

    private string _hostName = "—";
    public string HostName { get => _hostName; private set => SetProperty(ref _hostName, value); }

    private string _hostDetails = "";
    public string HostDetails { get => _hostDetails; private set => SetProperty(ref _hostDetails, value); }

    private double _hpPercent;
    public double HpPercent { get => _hpPercent; private set => SetProperty(ref _hpPercent, value); }

    private string _hpText = "—";
    public string HpText { get => _hpText; private set => SetProperty(ref _hpText, value); }

    private double _mpPercent;
    public double MpPercent { get => _mpPercent; private set => SetProperty(ref _mpPercent, value); }

    private string _mpText = "—";
    public string MpText { get => _mpText; private set => SetProperty(ref _mpText, value); }

    private bool _hasTarget;
    public bool HasTarget { get => _hasTarget; private set => SetProperty(ref _hasTarget, value); }

    private string _targetName = "Нет цели";
    public string TargetName { get => _targetName; private set => SetProperty(ref _targetName, value); }

    private string _targetDetails = "";
    public string TargetDetails { get => _targetDetails; private set => SetProperty(ref _targetDetails, value); }

    private bool _hasPet;
    public bool HasPet { get => _hasPet; private set => SetProperty(ref _hasPet, value); }

    private string _petTitle = "Пета нет";
    public string PetTitle { get => _petTitle; private set => SetProperty(ref _petTitle, value); }

    private double _petHpPercent;
    public double PetHpPercent { get => _petHpPercent; private set => SetProperty(ref _petHpPercent, value); }

    private string _petHpText = "";
    public string PetHpText { get => _petHpText; private set => SetProperty(ref _petHpText, value); }

    private string _petDetails = "";
    public string PetDetails { get => _petDetails; private set => SetProperty(ref _petDetails, value); }

    private string _botState = "Ожидание";
    public string BotState { get => _botState; private set => SetProperty(ref _botState, value); }

    private string _snapshotInfo = "";
    public string SnapshotInfo { get => _snapshotInfo; private set => SetProperty(ref _snapshotInfo, value); }

    // ── Лог и команды ───────────────────────────────────────────────────────

    public ObservableCollection<LogEntry> Log { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearLogCommand { get; }

    private bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    private bool _isStopping;

    /// <summary>Бот останавливается: дожидается своего хода (вызов в игру — до 5 с). «Старт» пока нельзя.</summary>
    public bool IsStopping
    {
        get => _isStopping;
        private set => SetProperty(ref _isStopping, value);
    }

    /// <summary>Спросить пользователя «да/нет» (окно ставит MessageBox).</summary>
    public Func<string, bool> AskYesNo { get; set; } = _ => false;

    /// <summary>Сообщить пользователю об ошибке (окно ставит MessageBox).</summary>
    public Action<string> Tell { get; set; } = _ => { };

    public void Start() => Start(CallTransport.Window);

    private void Start(CallTransport transport)
    {
        if (IsRunning || IsStopping || _connection is null)
            return;

        // Обход — с точки, выбранной в списке (не выбрана — с первой)
        var routeStart = SelectedRoutePoint is { } start ? Math.Max(0, RouteRows.IndexOf(start)) : 0;
        var result = _connection.StartBot(Settings.Mode, Settings, routeStart, transport);
        switch (result.Status)
        {
            case StartStatus.Started:
                IsRunning = true;
                BotState = "Запуск…";
                _log.Info($"Старт: {BotModes.Title(Settings.Mode)}{(transport == CallTransport.Thread ? " (вызовы отдельным потоком)" : "")}");
                break;

            // Основной способ не вышел — сам на поток не переходим (на нём падал 1.4.6): спрашиваем. Выбор не запоминаем
            case StartStatus.WindowFailed:
                _log.Warning($"Вызовы через окно игры не подключились: {result.Problem}");
                if (AskYesNo($"Не удалось подключиться к окну игры ({result.Problem}).\n\nМожно попробовать запасной способ — он работает, " +
                             "но на некоторых клиентах игра от него падает. Попробовать?"))
                    Start(CallTransport.Thread);
                else
                    _log.Info("Запасной способ не выбран — бот не запущен");
                break;

            default:
                _log.Error($"Бот не может управлять этим клиентом: {result.Problem}");
                Tell($"Бот не может управлять этим клиентом:\n{result.Problem}");
                break;
        }
    }

    /// <summary>Остановить бота: сразу «Останавливаю…», ожидание его хода — в фоне, окно не замирает.</summary>
    public void Stop()
    {
        if (!IsRunning || _connection is not { } connection)
            return;

        IsRunning = false;
        IsStopping = true;
        BotState = "Останавливаю…";
        connection.StopBotAsync().ContinueWith(_ => OnUi(() =>
        {
            _log.Info("Стоп");
            IsStopping = false;
            BotState = "Ожидание";
            CommandManager.InvalidateRequerySuggested();
        }));
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text.Substring(1);

    // ── Настройки бота ──────────────────────────────────────────────────────

    private int _tab;

    /// <summary>Вкладка в середине окна: 0 — бот, 1 — настройки, 2 — лог, 3 — ресы, 4 — кликер.</summary>
    public int Tab
    {
        get => _tab;
        set => SetProperty(ref _tab, value);
    }

    private string _lastEvent = "";

    /// <summary>Последняя запись лога — видна на вкладке «Бот».</summary>
    public string LastEvent
    {
        get => _lastEvent;
        private set => SetProperty(ref _lastEvent, value);
    }

    /// <summary>Настройки окна. Поля привязаны напрямую; после правки окно зовёт <see cref="SettingsEdited"/>.</summary>
    public BotSettings Settings => _config.Current;

    public sealed record SkillChoice(int Id, string Title);

    /// <summary>Атакующие скиллы персонажа (изученные, кроме лечения/воскрешения/портала) с названиями из игры.</summary>
    public ObservableCollection<SkillChoice> AttackSkills { get; } = new();

    /// <summary>Мобы для белого списка — выбираются из списка (TagPicker), не вводятся руками.</summary>
    public ObservableCollection<string> MobNames { get; } = new();

    /// <summary>Предметы для белого/чёрного списка лута.</summary>
    public ObservableCollection<string> LootNames { get; } = new();

    /// <summary>Ресурсы для белого/чёрного списка копания в радиусе фарма (варианты — <see cref="RouteOptions"/>).</summary>
    public ObservableCollection<string> FarmResourceNames { get; } = new();

    /// <summary>Опасные мобы обхода по названию (боссы) — выбираются из мобов вокруг.</summary>
    public ObservableCollection<string> DangerNames { get; } = new();

    // Всё, что бот видел за сессию: можно выбрать моба, который сейчас ушёл из виду
    private readonly Dictionary<string, NameCount> _seenMobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenItems = new(StringComparer.OrdinalIgnoreCase);
    private bool _syncingLists;

    public Func<IReadOnlyList<PickOption>> MobOptions
        => () => PickOptions(_lastWorld is null ? [] : NearbyNames.Mobs(_lastWorld), _seenMobs.Values);

    public Func<IReadOnlyList<PickOption>> LootOptions
        => () => PickOptions(_lastWorld is null ? [] : NearbyNames.GroundItems(_lastWorld), _seenItems.Select(n => new NameCount(n, 0, 0)));

    // Сначала то, что вокруг сейчас, потом — встреченное за сессию (с пометкой «не рядом»)
    private static IReadOnlyList<PickOption> PickOptions(IReadOnlyList<NameCount> nearby, IEnumerable<NameCount> seen)
    {
        var options = nearby.Select(n => new PickOption(n.Name, n.ToString())).ToList();
        var near = new HashSet<string>(nearby.Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        options.AddRange(seen.Where(n => !near.Contains(n.Name)).OrderBy(n => n.Name).Select(n => new PickOption(n.Name, $"{n} — не рядом")));
        return options;
    }

    /// <summary>Списки в окне ← настройки персонажа (при загрузке/смене персонажа).</summary>
    private void LoadNameLists()
    {
        _syncingLists = true;
        try
        {
            MobNames.Clear();
            foreach (var name in Settings.Target.MobNames)
                MobNames.Add(name);
            LootNames.Clear();
            foreach (var name in Settings.Loot.ItemNames)
                LootNames.Add(name);
            FarmResourceNames.Clear();
            foreach (var name in Settings.Loot.ResourceNames)
                FarmResourceNames.Add(name);
            DangerNames.Clear();
            foreach (var name in Settings.Route.DangerMobs)
                DangerNames.Add(name);
        }
        finally
        {
            _syncingLists = false;
        }
    }

    /// <summary>Выбрали/убрали название в окне → в настройки персонажа.</summary>
    private void NameListsEdited()
    {
        if (_syncingLists)
            return;

        Settings.Target.MobNames = MobNameFilter.Clean(MobNames);
        Settings.Loot.ItemNames = MobNameFilter.Clean(LootNames);
        Settings.Loot.ResourceNames = MobNameFilter.Clean(FarmResourceNames);
        Settings.Route.DangerMobs = MobNameFilter.Clean(DangerNames);
        SettingsEdited();
    }

    public IReadOnlyList<LootListMode> LootModes { get; } = [LootListMode.All, LootListMode.OnlyListed, LootListMode.ExceptListed];

    // ── Центр фарма ─────────────────────────────────────────────────────────

    /// <summary>Пункт списка «центр фарма» вместо сохранённой точки.</summary>
    public const string StartCenter = "Точка старта";

    /// <summary>«Точка старта» и сохранённые точки персонажа.</summary>
    public ObservableCollection<string> FarmCenters { get; } = new();

    public ICommand AddFarmPointCommand { get; }
    public ICommand RemoveFarmPointCommand { get; }

    private bool _syncingCenters;

    public string SelectedFarmCenter
    {
        get => Settings.Target.SelectedFarmPoint?.Name ?? StartCenter;
        set
        {
            // Пересборка списка сбрасывает выбор — это не выбор пользователя
            if (_syncingCenters || value is null)
                return;

            Settings.Target.FarmCenter = value == StartCenter ? "" : value;
            FarmCenterChanged();
        }
    }

    /// <summary>Выбрана сохранённая точка (а не точка старта).</summary>
    public bool HasFarmPoint => Settings.Target.SelectedFarmPoint is not null;

    /// <summary>Название выбранной точки; правка — переименование.</summary>
    public string FarmPointName
    {
        get => Settings.Target.SelectedFarmPoint?.Name ?? "";
        set
        {
            var point = Settings.Target.SelectedFarmPoint;
            var name = value?.Trim() ?? "";
            if (point is null || name.Length == 0 || name == point.Name)
                return;
            if (name.Equals(StartCenter, StringComparison.OrdinalIgnoreCase)
                || Settings.Target.FarmPoints.Any(p => p != point && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                _log.Warning($"Точка «{name}» уже есть — название не меняю");
                OnPropertyChanged();
                return;
            }

            point.Name = name;
            Settings.Target.FarmCenter = name;
            LoadFarmCenters();
            SettingsEdited();
        }
    }

    private string _farmPointInfo = "";

    /// <summary>Сколько до выбранной точки отсюда.</summary>
    public string FarmPointInfo { get => _farmPointInfo; private set => SetProperty(ref _farmPointInfo, value); }

    /// <summary>Запомнить, где стоит персонаж, как новую точку фарма, и сделать её центром.</summary>
    private void AddFarmPoint()
    {
        if (_lastWorld is not { } w)
            return;

        var points = Settings.Target.FarmPoints;
        var n = 1;
        while (points.Any(p => p.Name.Equals($"Точка {n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        var point = FarmPoint.At($"Точка {n}", w.Host.Position);
        points.Add(point);
        Settings.Target.FarmCenter = point.Name;
        _log.Info($"Точка фарма «{point.Name}» сохранена: {point.Position}");
        LoadFarmCenters();
        SettingsEdited();
    }

    private void RemoveFarmPoint()
    {
        if (Settings.Target.SelectedFarmPoint is not { } point)
            return;

        Settings.Target.FarmPoints.Remove(point);
        Settings.Target.FarmCenter = "";
        _log.Info($"Точка фарма «{point.Name}» удалена — центр: точка старта");
        LoadFarmCenters();
        SettingsEdited();
    }

    /// <summary>Список в окне ← точки персонажа.</summary>
    private void LoadFarmCenters()
    {
        _syncingCenters = true;
        try
        {
            FarmCenters.Clear();
            FarmCenters.Add(StartCenter);
            foreach (var point in Settings.Target.FarmPoints)
                FarmCenters.Add(point.Name);
        }
        finally
        {
            _syncingCenters = false;
        }

        FarmCenterChanged();
    }

    private void FarmCenterChanged()
    {
        OnPropertyChanged(nameof(SelectedFarmCenter));
        OnPropertyChanged(nameof(HasFarmPoint));
        OnPropertyChanged(nameof(FarmPointName));
        UpdateFarmPointInfo();
    }

    private void UpdateFarmPointInfo()
        => FarmPointInfo = Settings.Target.SelectedFarmPoint is { } p && _lastWorld is { } w
            ? $"{w.Host.Position.HorizontalDistanceTo(p.Position):0} м отсюда"
            : "";

    // ── Точки ресурсов (блокнот — в фоне, в окне не показывается) ───────────

    /// <summary>Список точек маршрута в окне — раз в секунду: расстояния и какая сейчас текущая.</summary>
    private void ShowSpots(WorldState w, bool force = false)
    {
        var now = DateTime.Now;
        if (!force && now - _spotsShown < SpotsShowEvery)
            return;
        _spotsShown = now;

        var here = w.Host.Position;
        var current = _connection?.Bot?.Brain.Part<IRouteProgress>()?.Index;
        foreach (var row in RouteRows)
            row.Update(here.HorizontalDistanceTo(row.Point.Position), IsRunning && current == RouteRows.IndexOf(row));
    }

    // ── Маршрут обхода (режим «Собирать ресурсы») ─────────────────────────────

    /// <summary>Точки обхода по порядку: после последней бот идёт к первой.</summary>
    public ObservableCollection<RouteRow> RouteRows { get; } = new();

    /// <summary>Список «что копать» выбранной точки (для её режима: только эти / всё, кроме этих).</summary>
    public ObservableCollection<string> RouteNames { get; } = new();

    public ICommand AddRoutePointCommand { get; }
    public ICommand RemoveRoutePointCommand { get; }
    public ICommand RoutePointUpCommand { get; }
    public ICommand RoutePointDownCommand { get; }

    private RouteRow? _selectedRoutePoint;

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
            SettingsEdited();
        }
    }

    /// <summary>
    /// Радиус поиска ресурсов вокруг точки, м. Поле не переписывается на ходу (иначе «1» по дороге к «100» сразу становилось 5):
    /// границы 5–500 применяет копия настроек для бота.
    /// </summary>
    public int RouteRadius
    {
        get => Settings.Route.Radius;
        set
        {
            if (value == Settings.Route.Radius)
                return;
            Settings.Route.Radius = value;
            SettingsEdited();
        }
    }

    /// <summary>Опасны агрессивные мобы от этого уровня (0 — только список). Границы — у копии настроек для бота, как у радиуса.</summary>
    public int DangerLevel
    {
        get => Settings.Route.DangerLevel;
        set
        {
            if (value == Settings.Route.DangerLevel)
                return;
            Settings.Route.DangerLevel = value;
            SettingsEdited();
        }
    }

    /// <summary>Запас к радиусу агра, м (не меньше 1 — у копии настроек для бота).</summary>
    public int DangerMargin
    {
        get => Settings.Route.DangerMargin;
        set
        {
            if (value == Settings.Route.DangerMargin)
                return;
            Settings.Route.DangerMargin = value;
            SettingsEdited();
        }
    }

    /// <summary>
    /// Варианты ресурсов (для точки обхода и для копания в фарме): рядом (и «нересурсы» — их можно копать, если назвать), потом
    /// известные по блокноту.
    /// </summary>
    public Func<IReadOnlyList<PickOption>> RouteOptions
        => () => PickOptions(
            _lastWorld is null ? [] : NearbyNames.Resources(_lastWorld),
            _spots.Spots.Select(s => s.Name).Distinct(StringComparer.OrdinalIgnoreCase).Select(n => new NameCount(n, 0, 0)));

    private void AddRoutePoint()
    {
        if (_lastWorld is not { } w)
            return;

        var points = Settings.Route.Points;
        var n = 1;
        while (points.Any(p => p.Name.Equals($"Точка {n}", StringComparison.OrdinalIgnoreCase)))
            n++;
        var point = RoutePoint.At($"Точка {n}", w.Host.Position);
        points.Add(point);
        _log.Info($"Маршрут: точка {points.Count} {point.Position}{(w.Host.Flying == true ? " (в воздухе)" : "")}");
        LoadRoute();
        SelectedRoutePoint = RouteRows.LastOrDefault();
        SettingsEdited();
    }

    private void RemoveRoutePoint()
    {
        if (SelectedRoutePoint is not { } row)
            return;

        Settings.Route.Points.Remove(row.Point);
        _log.Info($"Маршрут: точка {RouteRows.IndexOf(row) + 1} удалена, осталось {Settings.Route.Points.Count}");
        LoadRoute();
        SelectedRoutePoint = null;
        SettingsEdited();
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
        LoadRoute();
        SelectedRoutePoint = RouteRows[to];
        OnPropertyChanged(nameof(SelectedRouteTitle));
        SettingsEdited();
    }

    /// <summary>Маршрут в окне ← настройки персонажа (выбор сбрасывается, если точки больше нет).</summary>
    private void LoadRoute()
    {
        var selected = SelectedRoutePoint?.Point;
        RouteRows.Clear();
        var points = Settings.Route.Points;
        for (var i = 0; i < points.Count; i++)
            RouteRows.Add(new RouteRow(i + 1, points[i]));
        if (_lastWorld is { } w)
        {
            foreach (var row in RouteRows)
                row.Update(w.Host.Position.HorizontalDistanceTo(row.Point.Position), false);
        }

        SelectedRoutePoint = RouteRows.FirstOrDefault(r => r.Point == selected);
        LoadPointNames();
        OnPropertyChanged(nameof(RouteRadius));
        OnPropertyChanged(nameof(DangerLevel));
        OnPropertyChanged(nameof(DangerMargin));
    }

    /// <summary>Список «что копать» в окне ← выбранная точка.</summary>
    private void LoadPointNames()
    {
        _syncingLists = true;
        try
        {
            RouteNames.Clear();
            foreach (var name in SelectedRoutePoint?.Point.Resources ?? [])
                RouteNames.Add(name);
        }
        finally
        {
            _syncingLists = false;
        }
    }

    private void RouteNamesEdited()
    {
        if (_syncingLists || SelectedRoutePoint is not { } row)
            return;

        row.Point.Resources = MobNameFilter.Clean(RouteNames);
        row.Refresh();
        SettingsEdited();
    }

    // ── Пет по среде ─────────────────────────────────────────────────────────

    /// <summary>Пункт списка «кого звать»: значение — название питомца, пусто — «авто».</summary>
    public sealed record PetChoice(string Value, string Title);

    /// <summary>Кого звать на земле: «авто» и питомцы, которые живут на земле.</summary>
    public ObservableCollection<PetChoice> GroundPets { get; } = new();

    /// <summary>Кого звать в воздухе: «авто» и питомцы, которые летают.</summary>
    public ObservableCollection<PetChoice> AirPets { get; } = new();

    /// <summary>Кого звать в воде: «авто» и питомцы, которые живут в воде.</summary>
    public ObservableCollection<PetChoice> WaterPets { get; } = new();

    private bool _knowsPetHabitats;
    private bool _syncingPets;

    /// <summary>Сервер говорит, где питомцы живут: выбор питомцами; иначе — номер клетки, как раньше.</summary>
    public bool KnowsPetHabitats { get => _knowsPetHabitats; private set => SetProperty(ref _knowsPetHabitats, value); }

    public string GroundPet
    {
        get => Settings.Pet.GroundPet;
        set => SetPet(value, () => Settings.Pet.GroundPet, v => Settings.Pet.GroundPet = v);
    }

    public string AirPet
    {
        get => Settings.Pet.AirPet;
        set => SetPet(value, () => Settings.Pet.AirPet, v => Settings.Pet.AirPet = v);
    }

    public string WaterPet
    {
        get => Settings.Pet.WaterPet;
        set => SetPet(value, () => Settings.Pet.WaterPet, v => Settings.Pet.WaterPet = v);
    }

    // Пересборка списка сбрасывает выбор — это не выбор пользователя
    private void SetPet(string? value, Func<string> get, Action<string> set)
    {
        if (_syncingPets || value is null || value == get())
            return;
        set(value);
        SettingsEdited();
    }

    /// <summary>Списки питомцев ← клетки (пересобираются, только когда что-то поменялось: питомцы, названия, «авто»).</summary>
    private void UpdatePetChoices(WorldState w)
    {
        var cages = w.Pet?.Cages ?? [];
        KnowsPetHabitats = cages.Any(p => p.Habitat is not null);
        if (!KnowsPetHabitats)
            return;

        Sync(GroundPets, Choices(cages, PetHabitat.Ground, Settings.Pet.GroundPet), nameof(GroundPet));
        Sync(AirPets, Choices(cages, PetHabitat.Air, Settings.Pet.AirPet), nameof(AirPet));
        Sync(WaterPets, Choices(cages, PetHabitat.Water, Settings.Pet.WaterPet), nameof(WaterPet));
    }

    private static List<PetChoice> Choices(IReadOnlyList<PetInCage> cages, PetHabitat where, string chosen)
    {
        var fit = cages.Where(p => p.Lives(where) && p.Name is not null).OrderBy(p => p.Cage).ToList();
        var auto = fit.FirstOrDefault() is { } first ? $"авто — {first.Name} (клетка {first.Cage})" : "авто — подходящего нет";
        var list = new List<PetChoice> { new("", auto) };
        list.AddRange(fit.Select(p => new PetChoice(p.Name!, $"{p.Name} (клетка {p.Cage})")));
        if (chosen.Length > 0 && !list.Any(c => string.Equals(c.Value, chosen, StringComparison.OrdinalIgnoreCase)))
            list.Add(new PetChoice(chosen, $"{chosen} — нет в клетках"));
        return list;
    }

    private void Sync(ObservableCollection<PetChoice> target, List<PetChoice> choices, string property)
    {
        if (choices.SequenceEqual(target))
            return;

        _syncingPets = true;
        try
        {
            target.Clear();
            foreach (var choice in choices)
                target.Add(choice);
        }
        finally
        {
            _syncingPets = false;
        }

        OnPropertyChanged(property);
    }

    public sealed record PathChoice(ApproachPath Path, string Title);

    public IReadOnlyList<PathChoice> ApproachPaths { get; } = [new(ApproachPath.Smart, "Умно"), new(ApproachPath.Direct, "Прямо")];

    /// <summary>Галочка «Пет только на время боя» — только у сервера, где найден отзыв пета.</summary>
    public bool CanRecallPet => _profile.Capabilities.Has(Capability.RecallPet);

    /// <summary>Выбор «Умно/Прямо» — только у сервера с автопутём; у остальных бег всегда по прямой.</summary>
    public bool CanChoosePath => _profile.Capabilities.Has(Capability.SmartMove);

    /// <summary>Любая правка настройки: сохранить файл и отдать копию работающему боту.</summary>
    public void SettingsEdited()
    {
        SaveCharacter();
        _connection?.Bot?.UpdateSettings(Settings);
    }

    private void UpdateAttackSkills(WorldState w)
    {
        // Названия скиллов догружаются в фоне — список пересобирается, когда изменились ID или названия
        var choices = w.Skills
            .Where(s => !_profile.Data.Skills.NotAttack.Contains(s.Id))
            .Select(s => new SkillChoice(s.Id, s.Name is null ? $"скилл {s.Id}" : $"{s.Name} ({s.Id})"))
            .ToList();
        if (choices.SequenceEqual(AttackSkills))
            return;

        AttackSkills.Clear();
        foreach (var choice in choices)
            AttackSkills.Add(choice);

        // Выбранного нет среди изученных — атакующий скилл класса по умолчанию, иначе первый
        var ids = choices.Select(c => c.Id).ToList();
        if (ids.Count > 0 && !ids.Contains(Settings.Combat.AttackSkillId))
        {
            Settings.Combat.AttackSkillId = ids.Contains(_profile.Data.Skills.DefaultAttack) ? _profile.Data.Skills.DefaultAttack : ids[0];
            SettingsEdited();
        }

        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(GroundPet));
        OnPropertyChanged(nameof(AirPet));
        OnPropertyChanged(nameof(WaterPet));
    }

    public void Dispose()
    {
        Disconnect();
        _spots.Save();
    }

    // ── Внутреннее ──────────────────────────────────────────────────────────

    private void RefreshClients() => RefreshClients(reconnect: false);

    private void RefreshClients(bool reconnect)
    {
        var keep = _selectedClient?.Pid;
        var clients = ClientList.Build(new SystemClientSource(_profile.Data), _profile.Data.ClientProcessName);

        _refreshing = true;
        try
        {
            Clients.Clear();
            foreach (var client in clients)
                Clients.Add(client);
            SelectedClient = ClientList.KeepSelection(clients, keep, _config.App.Connection.LastCharacter, ClientLock.IsTaken);
        }
        finally
        {
            _refreshing = false;
        }

        if (clients.Count == 0)
            _connectionLog.Warning("Клиенты игры не найдены — запустите игру и нажмите «обновить»");

        RenameAll();
        if (reconnect || SelectedClient?.Pid != _connection?.Pid || _connection is null || _connection.HasExited)
            Connect(SelectedClient);
    }

    private void RenameAll()
    {
        if (!RenameWindows)
            return;

        foreach (var client in Clients.Where(c => c.Nick is not null))
        {
            if (NativeWindows.GetTitle(client.Window) != client.WindowTitle && NativeWindows.SetTitle(client.Window, client.WindowTitle))
                _connectionLog.Info($"Окно PID {client.Pid} → «{client.WindowTitle}»");
        }
    }

    private void Connect(GameClient? client)
    {
        Disconnect();
        if (client is null)
        {
            ConnectionText = "Нет подключения";
            return;
        }

        try
        {
            var connection = _connection = ClientConnection.Open(client.Pid, _profile.Data, _logger);
            _spots.Server = _profile.Id;
            connection.WorldUpdated += state => OnUi(() => Show(state));
            connection.WorldFailed += message => OnUi(() => ShowFailure(message));
            connection.BotStatusChanged += status => OnUi(() => BotState = Capitalize(status));
            // Из хода бота — остановку не здесь, а в потоке окна (бот ждёт как раз этот ход)
            connection.BotStopRequested += _ => OnUi(Stop);

            IsConnected = true;
            ConnectionText = client.Nick ?? $"PID {client.Pid}";
            _connectionLog.Info($"Подключено: {client.Display}, сервер {_profile.Name}");
            connection.Start();
            connection.SetUnfreeze(Unfreeze);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Disconnect();
            ConnectionText = "Ошибка подключения";
            _connectionLog.Error($"Не удалось подключиться к PID {client.Pid}: {e.Message}");
        }
    }

    /// <summary>
    /// Отключиться. Подключение сначала останавливает бота и ждёт его ход — до 7 с в потоке окна: окно может замереть, если
    /// сменить клиент как раз во время вызова в игру (обычно ожидание — миллисекунды).
    /// </summary>
    private void Disconnect()
    {
        var wasRunning = IsRunning;
        IsRunning = false;
        _connection?.Dispose();
        _connection = null;
        if (wasRunning)
            _log.Info("Стоп");
        IsStopping = false;
        BotState = "Ожидание";
        IsConnected = false;
        ClearWorld();
    }

    private void ApplyUnfreeze() => _connection?.SetUnfreeze(Unfreeze);

    private void Show(WorldState w)
    {
        if (_connection is null)
            return;

        _lastWorld = w;
        HasPets = w.Pet is not null;
        UpdatePetChoices(w);
        foreach (var kind in NearbyNames.Mobs(w))
        {
            // Уровни вида копятся за сессию: «Волк (ур. 10–12)», даже если сейчас рядом только один
            _seenMobs[kind.Name] = _seenMobs.TryGetValue(kind.Name, out var seen) && seen.MaxLevel > 0
                ? kind with { MinLevel = Math.Min(seen.MinLevel, kind.MinLevel), MaxLevel = Math.Max(seen.MaxLevel, kind.MaxLevel) }
                : kind;
        }
        foreach (var item in w.GroundItems.Where(NearbyNames.IsLootItem))
            _seenItems.Add(item.Name.Trim());
        var h = w.Host;
        if (h.Name.Length > 0)
            SwitchCharacter(h.Name);
        ConnectionText = h.Name;
        HostName = h.Name;
        HostDetails = $"ур. {h.Level}" + (h.IsCasting ? " · кастует" : "") + (h.IsDead ? " · мёртв" : "");
        HpPercent = Percent(h.Hp, h.MaxHp);
        HpText = $"{h.Hp} / {h.MaxHp}";
        MpPercent = h.MaxMp is int maxMp ? Percent(h.Mp, maxMp) : 100;
        MpText = h.MaxMp is null ? $"{h.Mp}" : $"{h.Mp} / {h.MaxMp}";

        var target = w.Target;
        HasTarget = target is not null;
        TargetName = target?.Name ?? (h.TargetWid == 0 ? "Нет цели" : "Цель вне списка мобов");
        TargetDetails = target is null ? "" : $"HP {target.Hp} · {target.Offset} · {StateText(target, w)}";

        var pet = w.Pet;
        var active = pet?.ActiveCage is int cage ? pet.InCage(cage) : null;
        HasPet = active is not null;
        PetTitle = pet is null ? "Пета нет" : active is null ? "Пет не призван" : $"Пет · клетка {active.Cage}";
        PetHpPercent = active?.HpPercent ?? 0;
        PetHpText = active is null ? "" : $"{active.HpPercent} %";
        PetDetails = active is null ? "" : active.IsHungry ? "голоден" : "сыт";

        UpdateAttackSkills(w);
        UpdateFarmPointInfo();
        _spots.Observe(w);
        ShowSpots(w);
        SnapshotInfo = $"мобов рядом {w.Mobs.Count(m => !m.IsDead)} · лута {w.GroundItems.Count}";
    }

    private void ShowFailure(string message)
    {
        if (_connection is { HasExited: true })
        {
            _connectionLog.Warning("Клиент игры закрыт");
            Disconnect();
            ConnectionText = "Клиент закрыт";
            RefreshClients();
            return;
        }

        ConnectionText = "Персонаж не в мире";
        ClearWorld();
        SnapshotInfo = message;
    }

    // Мир не читается (другой сервер, загрузка, отключились) — старые HP/MP/пет не показываем, как будто они верные
    private void ClearWorld()
    {
        _lastWorld = null;
        _spots.Forget();
        HostName = "—";
        HostDetails = "";
        HpPercent = 0;
        HpText = "—";
        MpPercent = 0;
        MpText = "—";
        HasTarget = false;
        TargetName = "Нет цели";
        TargetDetails = "";
        HasPet = false;
        PetTitle = "Пета нет";
        PetHpPercent = 0;
        PetHpText = "";
        PetDetails = "";
        SnapshotInfo = "";
    }

    private static string StateText(NpcInfo n, WorldState w)
    {
        var state = n.State.Text() ?? "";
        if (n.TargetWid != 0 && n.TargetWid == w.Host.Wid)
            state += ", бьёт вас";
        else if (n.TargetWid != 0 && n.TargetWid == w.Pet?.ActiveWid)
            state += ", бьёт пета";
        return state;
    }

    private static double Percent(int value, int max) => max <= 0 ? 0 : Math.Max(0, Math.Min(100, value * 100.0 / max));

    private void AddLog(LogEntry entry)
    {
        Log.Add(entry);
        LastEvent = $"{entry.Time:HH:mm:ss} {entry.Message}";
        while (Log.Count > MaxLogLines)
            Log.RemoveAt(0);
    }

    private void SaveSettings() => _config.SaveApp();

    private void SaveCharacter() => _config.SaveCurrent();

    /// <summary>Подключились к другому персонажу — берём его настройки (новый персонаж — копия общих) и отдаём боту.</summary>
    private void SwitchCharacter(string nick)
    {
        if (!_config.SwitchTo(nick))
            return;

        _spots.Character = nick;
        _connection?.Bot?.UpdateSettings(Settings);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(GroundPet));
        OnPropertyChanged(nameof(AirPet));
        OnPropertyChanged(nameof(WaterPet));
        LoadNameLists();
        LoadFarmCenters();
        LoadRoute();
        ModeChanged();
        OnPropertyChanged(nameof(SettingsOwner));
        AttackSkills.Clear(); // пересоберётся по снимку с учётом скилла этого персонажа
    }

    private WorldState? _lastWorld;

    private bool _hasPets = true;

    /// <summary>Есть ли у персонажа петы вообще (не друид — нет). Пока неизвестно — считаем, что есть.</summary>
    public bool HasPets
    {
        get => _hasPets;
        private set => SetProperty(ref _hasPets, value);
    }

    private bool _onlyImportantLog;

    /// <summary>В логе только предупреждения и ошибки.</summary>
    public bool OnlyImportantLog
    {
        get => _onlyImportantLog;
        set => SetProperty(ref _onlyImportantLog, value);
    }

    public sealed record ModeChoice(BotMode Mode, string Title);

    public IReadOnlyList<ModeChoice> Modes { get; } =
        [.. new[] { BotMode.FarmMobs, BotMode.GatherResources, BotMode.Clicker }.Select(m => new ModeChoice(m, BotModes.Title(m)))];

    /// <summary>Режим бота у этого персонажа. Смена во время работы останавливает бота.</summary>
    public BotMode Mode
    {
        get => Settings.Mode;
        set
        {
            if (Settings.Mode == value)
                return;

            if (IsRunning)
            {
                Stop();
                _log.Info("Режим сменён — бот остановлен, нажмите «Старт»");
            }

            Settings.Mode = value;
            SettingsEdited();
            ModeChanged();
        }
    }

    public bool ShowResourcesTab => Settings.Mode == BotMode.GatherResources;
    public bool ShowMobsTab => Settings.Mode == BotMode.FarmMobs;
    public bool ShowClickerTab => Settings.Mode == BotMode.Clicker;

    private void ModeChanged()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(ShowResourcesTab));
        OnPropertyChanged(nameof(ShowClickerTab));
        OnPropertyChanged(nameof(ShowMobsTab));
        // Вкладка исчезла — на «Бот»
        if ((Tab == TabResources && !ShowResourcesTab) || (Tab == TabClicker && !ShowClickerTab) || (Tab == TabMobs && !ShowMobsTab))
            Tab = 0;
    }

    public const int TabMobs = 1;
    public const int TabResources = 3;
    public const int TabClicker = 4;

    /// <summary>Чьи настройки сейчас на вкладках «Мобы» и «Общее».</summary>
    public string SettingsOwner => _config.Character is null ? "Общие настройки (персонаж не выбран)" : $"Настройки персонажа {_config.Character}";

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}

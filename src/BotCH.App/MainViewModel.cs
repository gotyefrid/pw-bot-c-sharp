using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BotCH.App.Mvvm;
using BotCH.App.Panels;
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
    private readonly SpotService _spots;

    // Правка настроек — сохранить файл и отдать боту копию через 0,5 с после последней (число в поле набирают по цифре)
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(0.5);
    private readonly DispatcherTimer _saveTimer = new() { Interval = SaveDelay };

    public MainViewModel(string appDirectory)
    {
        var ring = new RingBufferSink(LogPanel.MaxLines);
        var logger = new Logger().AddSink(ring).AddSink(new DailyFileSink(Path.Combine(appDirectory, "logs")));
        ring.Added += entry => OnUi(() => Log.Add(entry));
        _logger = logger;
        _log = logger.For("окно");
        _connectionLog = logger.For("подключение");

        _config = new SettingsService(appDirectory, _log);
        _saveTimer.Tick += (_, _) => FlushSettings();
        Settings.Edited += SettingsEdited;

        var shared = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BotCH");
        _spots = new SpotService(shared, logger.For("ресурсы"));
        _spots.ImportOld(Path.Combine(appDirectory, "resources.json"));

        Servers = _catalog.Ids.Select(id => _catalog.Load(id)).ToList();
        _profile = Servers.FirstOrDefault(s => s.Id == _config.App.Connection.ServerId) ?? Servers.First();

        Route = new RoutePanel(() => Settings, () => _lastWorld,
            () => IsRunning ? _connection?.Bot?.Brain.Part<IRouteProgress>()?.Index : null, _spots, _log, SettingsEdited);
        LoadNameLists();
        LoadFarmCenters();
        Route.Load();
        MobNames.CollectionChanged += (_, _) => NameListsEdited();
        LootNames.CollectionChanged += (_, _) => NameListsEdited();
        FarmResourceNames.CollectionChanged += (_, _) => NameListsEdited();

        RefreshCommand = new RelayCommand(RefreshClients);
        StartCommand = new RelayCommand(Start, () => IsConnected && !IsRunning && !IsStopping);
        StopCommand = new RelayCommand(Stop, () => IsRunning);

        AddFarmPointCommand = new RelayCommand(AddFarmPoint, () => _lastWorld is not null);
        RemoveFarmPointCommand = new RelayCommand(RemoveFarmPoint, () => HasFarmPoint);

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

    /// <summary>Персонаж, цель, пет — на вкладке «Бот».</summary>
    public StatusPanel Status { get; } = new();

    /// <summary>Лог в окне.</summary>
    public LogPanel Log { get; } = new();

    private string _botState = "Ожидание";
    public string BotState { get => _botState; private set => SetProperty(ref _botState, value); }

    // ── Команды ──────────────────────────────────────────────────────────────

    public ICommand RefreshCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

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
        var result = _connection.StartBot(Settings.Mode, Settings, Route.StartIndex, transport);
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

    /// <summary>Настройки окна. Поля привязаны напрямую; о правке сообщают сами (<see cref="BotSettings.Edited"/>).</summary>
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

    // ── Маршрут обхода (режим «Собирать ресурсы») ─────────────────────────────

    /// <summary>Вкладка «Ресы»: точки маршрута, что копать, опасные мобы.</summary>
    public RoutePanel Route { get; }

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

    /// <summary>
    /// Любая правка настройки (привязкой — сообщают сами настройки, из кода — так же или явным вызовом): через 0,5 с после
    /// последней — сохранить файл и отдать копию работающему боту. Поле не переписывается на ходу (иначе «1» по дороге к «100»
    /// сразу стало бы 5) — границы применяет копия для бота.
    /// </summary>
    private void SettingsEdited()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // Отложенная правка — сейчас (по таймеру, перед сменой персонажа и при закрытии)
    private void FlushSettings()
    {
        if (!_saveTimer.IsEnabled)
            return;

        _saveTimer.Stop();
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
        FlushSettings();
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
            // Блокноту ресурсов — каждый снимок, прямо в потоке снимков; окну — только последний
            connection.WorldUpdated += _spots.Observe;
            connection.WorldUpdated += ShowLatest;
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
        Interlocked.Exchange(ref _latest, null);
        if (wasRunning)
            _log.Info("Стоп");
        IsStopping = false;
        BotState = "Ожидание";
        IsConnected = false;
        ClearWorld();
    }

    private void ApplyUnfreeze() => _connection?.SetUnfreeze(Unfreeze);

    // Снимок, который окно ещё не показало. Окно не успевает (4 снимка в секунду) — промежуточные пропускаем: очередь
    // в поток окна не копится, показывается свежее
    private WorldState? _latest;

    private void ShowLatest(WorldState state)
    {
        if (Interlocked.Exchange(ref _latest, state) is null)
            OnUi(() =>
            {
                if (Interlocked.Exchange(ref _latest, null) is { } world)
                    Show(world);
            });
    }

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
        Status.Show(w);
        UpdateAttackSkills(w);
        UpdateFarmPointInfo();
        Route.ShowDistances(w);
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
        Status.SnapshotInfo = message;
    }

    // Мир не читается (другой сервер, загрузка, отключились) — старое состояние не показываем, как будто оно верное
    private void ClearWorld()
    {
        _lastWorld = null;
        _spots.Forget();
        Status.Clear();
    }

    private void SaveSettings() => _config.SaveApp();

    private void SaveCharacter() => _config.SaveCurrent();

    /// <summary>Подключились к другому персонажу — берём его настройки (новый персонаж — копия общих) и отдаём боту.</summary>
    private void SwitchCharacter(string nick)
    {
        if (nick == _config.Character)
            return;

        // Правки прошлого персонажа — в его файл, пока он ещё текущий
        FlushSettings();
        Settings.Edited -= SettingsEdited;
        _config.SwitchTo(nick);
        Settings.Edited += SettingsEdited;

        _connection?.Bot?.UpdateSettings(Settings);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(GroundPet));
        OnPropertyChanged(nameof(AirPet));
        OnPropertyChanged(nameof(WaterPet));
        LoadNameLists();
        LoadFarmCenters();
        Route.Load();
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

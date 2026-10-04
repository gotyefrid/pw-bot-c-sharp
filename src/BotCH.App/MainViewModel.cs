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
/// Модель главного окна: подключение к клиенту, запуск и остановка бота, режим, настройки персонажа (сохранение через 0,5 с).
/// Вкладки — свои маленькие классы (<see cref="Status"/>, <see cref="Farm"/>, <see cref="Pets"/>, <see cref="Route"/>,
/// <see cref="Log"/>): новая вкладка (кликер) — ещё один такой класс, а не поля здесь. Снимок мира читается в фоне
/// (<see cref="WorldMonitor"/>), в поток окна переносится только последний.
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

        Pets = new PetsPanel(() => Settings, SettingsEdited);
        Farm = new FarmPanel(() => Settings, () => _lastWorld, () => _profile, _log, SettingsEdited, () =>
        {
            // Список скиллов пересобран — выбор в окне перечитать заново
            OnPropertyChanged(nameof(Settings));
            Pets.Rebind();
        });
        Route = new RoutePanel(() => Settings, () => _lastWorld,
            () => IsRunning ? _connection?.Bot?.Brain.Part<IRouteProgress>()?.Index : null, _spots, _log, SettingsEdited);
        Farm.Load();
        Route.Load();

        RefreshCommand = new RelayCommand(RefreshClients);
        StartCommand = new RelayCommand(Start, () => IsConnected && !IsRunning && !IsStopping);
        StopCommand = new RelayCommand(Stop, () => IsRunning);

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

    /// <summary>
    /// Unfreeze текущего персонажа (у нового — выключен). В игру пишет, только когда персонаж уже прочитан на этом клиенте:
    /// раз прочитан — профиль сервера к клиенту подходит, и флаг ляжет туда, куда надо.
    /// </summary>
    public bool Unfreeze
    {
        get => Settings.Unfreeze == true;
        set
        {
            if (Unfreeze == value)
                return;

            Settings.Unfreeze = value;
            OnPropertyChanged();
            if (_unfreezeFor is not null)
                ApplyUnfreeze();
        }
    }

    /// <summary>Персонаж известен — его галки (unfreeze) можно менять.</summary>
    public bool HasCharacter => _config.Character is not null;

    // Для кого на этом подключении уже применён unfreeze; null — персонаж ещё не прочитан (в игру не пишем)
    private string? _unfreezeFor;

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

    /// <summary>Центр фарма, списки мобов, лута и ресурсов, атакующий скилл — вкладки «Мобы» и «Общее».</summary>
    public FarmPanel Farm { get; }

    /// <summary>Кого звать петом по среде.</summary>
    public PetsPanel Pets { get; }

    // ── Маршрут обхода (режим «Собирать ресурсы») ─────────────────────────────

    /// <summary>Вкладка «Ресы»: точки маршрута, что копать, опасные мобы.</summary>
    public RoutePanel Route { get; }

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

    /// <summary>Лог окна — для страховки выхода (<see cref="ExitWatchdog"/>), когда модель уже освобождена.</summary>
    internal ILogger WindowLog => _log;

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
        _unfreezeFor = null;
        Interlocked.Exchange(ref _latest, null);
        if (wasRunning)
            _log.Info("Стоп");
        IsStopping = false;
        BotState = "Ожидание";
        IsConnected = false;
        ClearWorld();
    }

    private void ApplyUnfreeze() => _connection?.SetUnfreeze(Unfreeze);

    // Персонаж прочитан на этом подключении (или сменился) — теперь его unfreeze, раньше в игру не пишем
    private void UnfreezeFor(string nick)
    {
        if (_unfreezeFor == nick)
            return;

        _unfreezeFor = nick;
        ApplyUnfreeze();
    }

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
        Pets.Observe(w);
        Farm.Observe(w);
        var h = w.Host;
        if (h.Name.Length > 0)
        {
            SwitchCharacter(h.Name);
            UnfreezeFor(h.Name);
        }
        ConnectionText = h.Name;
        Status.Show(w);
        Farm.ShowFor(w);
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
        Pets.Rebind();
        Farm.Load();
        Route.Load();
        ModeChanged();
        OnPropertyChanged(nameof(SettingsOwner));
        OnPropertyChanged(nameof(Unfreeze));
        OnPropertyChanged(nameof(HasCharacter));
    }

    private WorldState? _lastWorld;

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

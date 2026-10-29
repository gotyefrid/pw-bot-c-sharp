using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using Notes.App.Mvvm;
using Notes.App.Panels;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Clicker;
using Notes.Core.Calls;
using Notes.Core.Clients;
using Notes.Core.GameFiles;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.Resources;
using Notes.Core.Session;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.App;

/// <summary>
/// Модель главного окна: подключение к клиенту, запуск и остановка бота, режим, настройки персонажа (сохранение через 0,5 с).
/// Вкладки — свои маленькие классы (<see cref="Status"/>, <see cref="Farm"/>, <see cref="Pets"/>, <see cref="Route"/>,
/// <see cref="Clicker"/>, <see cref="Log"/>): новая вкладка — ещё один такой класс, а не поля здесь. Снимок мира читается в фоне
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

    // Подключение к клиенту: снимки и бот (он принадлежит подключению — окно только просит запустить и остановить)
    private ClientConnection? _connection;
    private ServerProfile _profile;
    private bool _refreshing;

    // Точки ресурсов — общие на все серверы, персонажей и копии бота (%AppData%\Notes\resources.json), копятся в любом режиме
    private readonly SpotService _spots;

    // Правка настроек — сохранить файл и отдать боту копию через 0,5 с после последней (число в поле набирают по цифре)
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(0.5);
    private readonly DispatcherTimer _saveTimer = new() { Interval = SaveDelay };

    // Вопросы и сообщения пользователю — окна бота (MessageBox, «Открыть»/«Сохранить как»)
    private readonly IDialogs _dialogs;

    public MainViewModel(string appDirectory, IDialogs dialogs)
    {
        _dialogs = dialogs;
        var ring = new RingBufferSink(LogPanel.MaxLines);
        var logger = new Logger().AddSink(ring).AddSink(new DailyFileSink(Path.Combine(appDirectory, "logs")));
        ring.Added += entry => UiThread.Run(() => Log.Add(entry));
        _logger = logger;
        _log = logger.For(LogSources.Window);
        _connectionLog = logger.For(LogSources.Connection);

        _config = new SettingsService(appDirectory, _log);
        // Что показывать в логе — своё у окна, как подключение: в settings.json
        Log.Load(_config.App.LogView);
        Log.FilterChanged += () =>
        {
            Log.Save(_config.App.LogView);
            SaveSettings();
        };
        _saveTimer.Tick += (_, _) => FlushSettings();
        Settings.Edited += SettingsEdited;

        var resourcesLog = logger.For(LogSources.Resources);
        var shared = SharedFolder.Prepare(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), resourcesLog);
        _spots = new SpotService(shared, resourcesLog);
        _spots.ImportOld(Path.Combine(appDirectory, "resources.json"));

        Servers = _catalog.Ids.Select(id => _catalog.Load(id)).ToList();
        _profile = Servers.FirstOrDefault(s => s.Id == _config.App.Connection.ServerId) ?? Servers.First();
        Patch = new ClientPatchPanel(() => _profile, () => _config.App.Connection, SaveSettings, dialogs, _connectionLog);
        // Сменилась «Игра» — в списке теперь клиенты её exe. Подключённого не подменяем: список заперт до отключения,
        // а после отключения его обновит ⟳
        Patch.GameChanged += () =>
        {
            if (!IsConnected)
                ReloadClients();
        };
        // Строка в свёрнутой карточке показывает и состояние патча
        Patch.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ClientPatchPanel.State))
                OnPropertyChanged(nameof(Summary));
        };

        Pets = new PetsPanel(() => Settings, SettingsEdited);
        Farm = new FarmPanel(() => Settings, () => _lastWorld, () => _profile, id => _connection?.SkillIcons.Get(id),
            macro => _connection?.MacroIcon(macro), _log, SettingsEdited, () =>
        {
            // Список скиллов пересобран — выбор в окне перечитать заново
            OnPropertyChanged(nameof(Settings));
            Pets.Rebind();
        });
        Route = new RoutePanel(() => Settings, () => _lastWorld,
            () => IsRunning ? _connection?.Bot?.Brain.Part<IRouteProgress>()?.Index : null, _spots, _log, SettingsEdited);
        Clicker = new ClickerPanel(() => Settings,
            () => IsRunning ? _connection?.Bot?.Brain.Part<IClickerProgress>()?.Current : null,
            new ClickerChoices(() => Farm.LootOptions(), () => Route.RouteOptions(), () => Farm.TargetOptions(),
                () => PickLists.Foods(_lastWorld?.Inventory ?? []), () => PickLists.Skills(_lastWorld?.Skills ?? [])), SettingsEdited, dialogs);
        Farm.Load();
        Route.Load();
        Clicker.Load();

        // Пока подключены, список заперт: обновление могло бы выбрать в нём не того клиента, к которому подключены
        RefreshCommand = new RelayCommand(RefreshClients, () => !IsConnected);
        ConnectCommand = new RelayCommand(() => Connect(anyway: false), () => !IsConnected && SelectedClient is not null);
        ConnectAnywayCommand = new RelayCommand(() => Connect(anyway: true), () => CanConnectAnyway);
        DisconnectCommand = new RelayCommand(Disconnect, () => IsConnected);
        ToggleConnectionCommand = new RelayCommand(() => ConnectionExpanded = !ConnectionExpanded);
        StartCommand = new RelayCommand(Start, () => IsConnected && !IsRunning && !IsStopping);
        StopCommand = new RelayCommand(Stop, () => IsRunning);

        _log.Info("Запущено");
    }

    /// <summary>
    /// Первое обновление списка клиентов — отдельно от конструктора: окно зовёт это по Loaded, когда оно уже показано.
    /// Только заполняет список, никуда не подключаясь.
    /// </summary>
    public void StartUp() => RefreshClients();

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
            OnPropertyChanged(nameof(Summary));
            // Читать клиента теперь по другим смещениям — старое подключение не годится
            Disconnect();
            RefreshClients();
        }
    }

    public ObservableCollection<GameClient> Clients { get; } = new();

    private GameClient? _selectedClient;

    public GameClient? SelectedClient
    {
        get => _selectedClient;
        set
        {
            if (!SetProperty(ref _selectedClient, value) || _refreshing)
                return;

            // Выбрали — не подключаемся: подключение только кнопкой «Подключить»
            ClearHint();
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

    private bool _isConnected;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetProperty(ref _isConnected, value))
                return;

            OnPropertyChanged(nameof(CanChoose));
            OnPropertyChanged(nameof(Summary));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Сервер и клиента можно менять, только пока не подключены: сменить персонажа = отключиться → выбрать → подключить.</summary>
    public bool CanChoose => !IsConnected;

    private string _connectionText = "";

    /// <summary>Кто подключён: ник из мира, пока не прочитан — ник клиента или PID.</summary>
    private string ConnectionText
    {
        get => _connectionText;
        set
        {
            if (SetProperty(ref _connectionText, value))
                OnPropertyChanged(nameof(Summary));
        }
    }

    // ── Карточка «Подключение»: свёрнутая строка, подсказка следующего шага ──

    /// <summary>Строка свёрнутой карточки: «Вася · Comeback 1.4.6 · патч ✓».</summary>
    public string Summary
    {
        get
        {
            var who = IsConnected ? (ConnectionText.Length > 0 ? ConnectionText : "Подключено") : "Не подключено";
            var patch = Patch.Recipe is null || !IsConnected ? ""
                : Patch.State == ClientPatchState.Patched ? " · патч ✓" : " · без патча";
            return $"{who} · {_profile.Name}{patch}";
        }
    }

    private bool _connectionExpanded = true;

    /// <summary>
    /// Карточка развёрнута. Сворачивается сама после подключения, разворачивается при отключении и закрытии клиента;
    /// вручную — в любой момент (между запусками не запоминается).
    /// </summary>
    public bool ConnectionExpanded
    {
        get => _connectionExpanded;
        set => SetProperty(ref _connectionExpanded, value);
    }

    private string _connectHint = "";

    /// <summary>Строка-причина под выбором клиента: что не так и что нажать дальше. Модальных окон в этом флоу нет.</summary>
    public string ConnectHint
    {
        get => _connectHint;
        private set
        {
            if (SetProperty(ref _connectHint, value))
                OnPropertyChanged(nameof(HasHint));
        }
    }

    public bool HasHint => ConnectHint.Length > 0;

    // Чем закончилась последняя проверка — от этого зависит, какая кнопка следующего шага видна
    private ConnectVerdict? _hintVerdict;

    public bool CanConnectAnyway => !IsConnected && _hintVerdict
        is ConnectVerdict.NotPatched or ConnectVerdict.UnknownBuild or ConnectVerdict.ProtectorChanged or ConnectVerdict.ProtectorUnknown;

    private void ShowHint(ConnectVerdict? verdict, string text)
    {
        _hintVerdict = verdict;
        ConnectHint = text;
        OnPropertyChanged(nameof(CanConnectAnyway));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearHint() => ShowHint(null, "");

    // ── Игра и патч (панель: путь, состояние, кнопки) ───────────────────────

    /// <summary>Строки «Игра» и «Патч»: exe сервера, состояние файла, кнопки «Выбрать…», «Пропатчить», «Откат».</summary>
    public ClientPatchPanel Patch { get; }

    // ── Состояние ────────────────────────────────────────────────────────────

    /// <summary>Персонаж, цель, пет — на вкладке «Бот».</summary>
    public StatusPanel Status { get; } = new();

    /// <summary>Лог в окне.</summary>
    public LogPanel Log { get; } = new();

    private string _botState = "Ожидание";
    public string BotState { get => _botState; private set => SetProperty(ref _botState, value); }

    // ── Команды ──────────────────────────────────────────────────────────────

    public ICommand RefreshCommand { get; }
    public ICommand ConnectCommand { get; }
    public ICommand ConnectAnywayCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ToggleConnectionCommand { get; }
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
                if (_dialogs.AskYesNo($"Не удалось подключиться к окну игры ({result.Problem}).\n\nМожно попробовать запасной способ — он работает, " +
                             "но на некоторых клиентах игра от него падает. Попробовать?"))
                    Start(CallTransport.Thread);
                else
                    _log.Info("Запасной способ не выбран — бот не запущен");
                break;

            default:
                _log.Error($"Бот не может управлять этим клиентом: {result.Problem}");
                _dialogs.Tell($"Не получается управлять этим клиентом:\n{result.Problem}");
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
        connection.StopBotAsync().ContinueWith(_ => UiThread.Run(() =>
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

    /// <summary>Вкладка «Кликер»: сценарий — шаги по кругу, у каждого действие и условие.</summary>
    public ClickerPanel Clicker { get; }

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

    /// <summary>⟳, запуск, смена сервера: список клиентов и состояние файла «Игры» — заново.</summary>
    private void RefreshClients()
    {
        ReloadClients();
        Patch.Refresh();
    }

    /// <summary>
    /// Перестроить список запущенных клиентов и выбрать последнего персонажа. Никуда не подключается. У сервера с
    /// «Игрой» в списке только клиенты из её exe; «Игра» не задана, а у всех клиентов сервера один exe — он и
    /// становится «Игрой».
    /// </summary>
    private void ReloadClients()
    {
        // Подсказка и её кнопки — про прежний список: после обновления там может быть выбран уже другой клиент
        ClearHint();
        var keep = _selectedClient?.Pid;
        var source = new SystemClientSource(_profile.Data);
        var processes = source.FindProcesses();
        var game = Patch.Recipe is null ? null : Patch.GamePath;
        var clients = ClientList.Build(source, processes, game);
        if (Patch.Recipe is not null && game is null && ClientList.GuessGame(clients) is { } guessed)
        {
            // Список перестроится уже по «Игре»: Adopt → GameChanged → сюда же
            Patch.Adopt(guessed);
            return;
        }

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

        var others = processes.Where(p => !ClientList.IsOfGame(p.ExePath, game)).ToList();
        if (processes.Count == 0)
            _connectionLog.Warning("Клиенты игры не найдены — запустите игру и нажмите «обновить»");
        else if (others.Count > 0)
            _connectionLog.Info("Не из exe «Игры», в списке не показаны: "
                                + string.Join(", ", others.Select(p => $"PID {p.Pid} — {p.ExePath ?? "путь не прочитать"}")));
        if (clients.Count == 0 && others.Count > 0)
            ShowHint(null, "Запущенные клиенты — не из exe «Игры». Чтобы подключиться к ним, укажите их exe кнопкой «Выбрать…».");

        RenameAll();
        CommandManager.InvalidateRequerySuggested();
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

    /// <summary>«Подключить»: проверка (<see cref="ConnectCheck"/>) → подключение. Не прошла — строка-подсказка и кнопка шага.</summary>
    private void Connect(bool anyway)
    {
        if (SelectedClient is not { } client)
            return;

        ClearHint();
        var alive = SystemClientSource.Running().Any(c => c.Pid == client.Pid);
        var recipe = Patch.Recipe;
        // «Игра» ещё не задана (клиенты сервера из разных exe) — ею становится exe клиента, к которому подключаемся
        if (alive && recipe is not null && Patch.GamePath is null && client.ExePath is { } path)
            Patch.Adopt(path);

        // Файл «Игры» — прямо сейчас, а не по прошлой проверке: его могли пропатчить или обновить после ⟳
        var state = alive && recipe is not null ? Patch.CheckNow() : ClientPatchState.Missing;
        var protector = CheckProtector(alive, client);
        var decision = ConnectCheck.Decide(recipe is not null, alive, state, anyway, protector);
        if (decision.Verdict != ConnectVerdict.Ready)
        {
            _connectionLog.Warning(decision.Text);
            ConnectionExpanded = true;
            ShowHint(decision.Verdict, decision.Text);
            return;
        }

        if (anyway)
            _connectionLog.Warning("Подключение к клиенту без проверки патча — по просьбе пользователя");

        Open(client);
    }

    /// <summary>
    /// Сторож рядом с клиентом: путь берётся от exe «Игры», а не от процесса — файл тот же, даже если клиент запущен
    /// из другого места. Эталона в профиле нет или клиента нет — проверять нечего (null). Подробности (path, размер,
    /// дата, sha256) — в лог: в окне игроку они ни к чему, а при разборе обновления нужны.
    /// </summary>
    private ProtectorState? CheckProtector(bool alive, GameClient client)
    {
        if (!alive)
            return null;

        var gamePath = Patch.GamePath ?? client.ExePath;
        if (gamePath is null)
            return null;

        var spec = _profile.Data.Protector;
        if (spec is not { IsConfigured: true })
            return null;

        var (state, file) = ProtectorCheck.Check(gamePath, spec);
        switch (state)
        {
            case ProtectorState.Changed when file is not null:
                _connectionLog.Warning($"Защита клиента обновилась: {file.Path}\n"
                                       + $"  {file.Length} байт, изменён {file.Modified:yyyy-MM-dd HH:mm}, "
                                       + $"sha256 {file.Sha256} (эталон {spec.Sha256})");
                break;
            case ProtectorState.Missing:
                _connectionLog.Warning($"Файл защиты клиента не найден рядом с «{gamePath}»");
                break;
            case ProtectorState.Unreadable:
                _connectionLog.Warning("Файл защиты клиента не читается (нет прав?)");
                break;
        }

        return state;
    }

    private void Open(GameClient client)
    {
        try
        {
            var connection = _connection = ClientConnection.Open(client.Pid, _profile.Data, _logger);
            _spots.Server = _profile.Id;
            // Блокноту ресурсов и логу урона — каждый снимок, прямо в потоке снимков; окну — только последний
            connection.WorldUpdated += _spots.Observe;
            connection.WorldUpdated += new DamageLog(_logger.For(LogSources.Damage)).Observe;
            connection.WorldUpdated += ShowLatest;
            connection.WorldFailed += message => UiThread.Run(() => ShowFailure(message));
            connection.BotStatusChanged += status => UiThread.Run(() => BotState = Capitalize(status));
            // Из хода бота — остановку не здесь, а в потоке окна (бот ждёт как раз этот ход)
            connection.BotStopRequested += _ => UiThread.Run(Stop);

            IsConnected = true;
            ConnectionText = client.Nick ?? $"PID {client.Pid}";
            ConnectionExpanded = false;
            _connectionLog.Info($"Подключено: {client.Display}, сервер {_profile.Name}");
            connection.Start();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Disconnect();
            // В окне — без подробностей: в тексте ошибки бывают адреса памяти
            ShowHint(null, "Не удалось подключиться — подробности в логе.");
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
        ConnectionText = "";
        ConnectionExpanded = true;
        ClearWorld();
    }

    // Снимок, который окно ещё не показало. Окно не успевает (4 снимка в секунду) — промежуточные пропускаем: очередь
    // в поток окна не копится, показывается свежее
    private WorldState? _latest;

    private void ShowLatest(WorldState state)
    {
        if (Interlocked.Exchange(ref _latest, state) is null)
            UiThread.Run(() =>
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
            SwitchCharacter(h.Name);
        ConnectionText = h.Name;
        Status.Show(w);
        Farm.ShowFor(w);
        Route.ShowDistances(w);
        Clicker.ShowCurrent();
    }

    private void ShowFailure(string message)
    {
        if (_connection is { HasExited: true })
        {
            _connectionLog.Warning("Клиент игры закрыт");
            Disconnect();
            RefreshClients();
            ShowHint(null, "Клиент игры закрыт — бот отключился.");
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
        Clicker.Load();
        ModeChanged();
        OnPropertyChanged(nameof(SettingsOwner));
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
}

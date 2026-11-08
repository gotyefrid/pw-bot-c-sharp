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
using Notes.Core.Incidents;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Notes.Core.Notify;
using Notes.Core.Profiles;
using Notes.Core.Resources;
using Notes.Core.Session;
using Notes.Core.Settings;
using Notes.Core.Stats;
using Notes.Core.World;

namespace Notes.App;

/// <summary>
/// Вкладка персонажа в главном окне: подключение к клиенту, запуск и остановка бота, режим, настройки персонажа (сохранение
/// через 0,5 с). У каждой вкладки своё подключение, бот, настройки персонажа и лог; общие настройки (settings.json) и отправка
/// в Telegram — одни на окно (<see cref="WindowServices"/>).
/// Разделы внутри вкладки («Бот», «Мобы», «Лог»…) — свои маленькие классы (<see cref="Status"/>, <see cref="Farm"/>, <see cref="Pets"/>, <see cref="Route"/>,
/// <see cref="Clicker"/>, <see cref="Log"/>): новая вкладка — ещё один такой класс, а не поля здесь. Снимок мира читается в фоне
/// (<see cref="WorldMonitor"/>), в поток окна переносится только последний.
/// </summary>
public sealed class CharacterTab : ObservableObject, IDisposable
{
    private readonly ProfileCatalog _catalog = ProfileCatalog.Default();

    // Общие настройки (settings.json: подключение + шаблон) и текущего персонажа (characters\<сервер>\Ник.json)
    private readonly SettingsService _config;
    private readonly ILogger _log;
    private readonly ILogger _connectionLog;
    private readonly Logger _logger;

    // Подключение к клиенту: снимки и бот (он принадлежит подключению — окно только просит запустить и остановить)
    private ClientConnection? _connection;
    // Запись мира вокруг смерти: одна на всё время работы — она же приёмник лога
    private readonly IncidentRecorder _incidents = new();
    private ServerProfile _profile;
    private bool _refreshing;

    // Точки ресурсов — общие на все серверы, персонажей и копии бота (%AppData%\Notes\resources.json), копятся в любом режиме
    private readonly SpotService _spots;
    // Маршруты обхода — общие у всех персонажей и копий бота
    private readonly RouteStore _routes;

    // Правка настроек — сохранить файл и отдать боту копию через 0,5 с после последней (число в поле набирают по цифре)
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(0.5);
    private readonly DispatcherTimer _saveTimer = new() { Interval = SaveDelay };

    // Уведомления в Telegram: отправка — одна на окно (общая у вкладок), слежение за чатом — своё у подключения
    private readonly TelegramSender _telegram;
    // Общие настройки окна: правку в другой вкладке — показать и здесь
    private readonly AppSettings _appSettings;
    private readonly ILogger _chatLog;
    private ChatNotifier? _notifier;

    // Вопросы и сообщения пользователю — окна бота (MessageBox, «Открыть»/«Сохранить как»)
    private readonly IDialogs _dialogs;

    // Куда пишутся файлы разбора (логи бота и код, присланный сервером): %exe%\logs
    private readonly string _logsDirectory;
    // Файл лога: общий, пока персонаж неизвестен, потом — logs\<сервер>\<дата> Ник.log
    private readonly DailyFileSink _fileLog;

    // Папка бота: в ней stats\<сервер>\Ник — статистика персонажа
    private readonly string _appDirectory;

    // «Персонажи»: логин, пароль и клиент каждого — characters\accounts.json, общий у копий бота из этой папки
    private readonly CharacterBook _book;

    /// <param name="services">Общее у вкладок окна: настройки settings.json и отправка в Telegram.</param>
    /// <param name="remembered">Вкладка из прошлого запуска (tabs.json); null — новая.</param>
    public CharacterTab(WindowServices services, IDialogs dialogs, OpenTab? remembered = null)
    {
        var appDirectory = services.AppDirectory;
        _dialogs = dialogs;
        _appDirectory = appDirectory;
        _logsDirectory = Path.Combine(appDirectory, "logs");
        var ring = new RingBufferSink(LogPanel.MaxLines);
        var logger = new Logger().AddSink(ring).AddSink(_fileLog = new DailyFileSink(_logsDirectory)).AddSink(_incidents);
        _incidents.Closed += SaveIncident;
        ring.Added += entry => UiThread.Run(() => Log.Add(entry));
        _logger = logger;
        _log = logger.For(LogSources.Window);
        _connectionLog = logger.For(LogSources.Connection);
        _chatLog = logger.For(LogSources.Chat);
        _telegram = services.Telegram;

        _appSettings = services.Settings;
        _config = new SettingsService(_appSettings, _log, owner: this);
        _appSettings.Saved += AppSettingsSaved;
        _book = new CharacterBook(Path.Combine(appDirectory, "characters", CharacterBook.FileName));
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
        _routes = new RouteStore(Path.Combine(shared, RouteStore.FileName));
        _spots.ImportOld(Path.Combine(appDirectory, "resources.json"));

        Servers = _catalog.Ids.Select(id => _catalog.Load(id)).ToList();
        // Вкладка из прошлого запуска — её сервер и персонаж; новая — сервер из общих настроек
        _wanted = remembered?.Nick;
        _profile = Servers.FirstOrDefault(s => s.Id == (remembered?.Server ?? _config.App.Connection.ServerId)) ?? Servers.First();
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
        Potions = new PotionsPanel(() => Settings, SettingsEdited);
        Repair = new RepairPanel(() => Settings, _log, SettingsEdited);
        Farm = new FarmPanel(() => Settings, () => _lastWorld, () => _profile, id => _connection?.SkillIcons.Get(id),
            macro => _connection?.MacroIcon(macro), _log, SettingsEdited, () =>
        {
            // Список скиллов пересобран — выбор в окне перечитать заново
            OnPropertyChanged(nameof(Settings));
            Pets.Rebind();
            Potions.Rebind();
            Repair.Rebind();
        });
        Route = new RoutePanel(() => Settings, () => _lastWorld,
            () => IsRunning ? _connection?.Bot?.Brain.Part<IRouteProgress>()?.Index : null, _spots, _log, SettingsEdited,
            _routes, () => _config.Character, dialogs);
        Clicker = new ClickerPanel(() => Settings,
            () => IsRunning ? _connection?.Bot?.Brain.Part<IClickerProgress>()?.Current : null,
            new ClickerChoices(() => Farm.LootOptions(), () => Route.RouteOptions(), () => Farm.TargetOptions(),
                () => PickLists.Foods(_lastWorld?.Inventory ?? []), () => PickLists.Skills(_lastWorld?.Skills ?? []),
                () => PickLists.PartyMembers(_lastWorld)), SettingsEdited, dialogs);
        Quests = new QuestsPanel(() => Settings, () => _lastWorld, SettingsEdited);
        Chat = new ChatPanel(() => _config.App.ChatNotify, () => _profile, SaveSettings, _chatLog);
        Farm.Load();
        Route.Load();
        Clicker.Load();
        Quests.Load();

        // Пока подключены, список заперт: обновление могло бы выбрать в нём не того клиента, к которому подключены
        RefreshCommand = new RelayCommand(RefreshClients, () => !IsConnected);
        ConnectCommand = new RelayCommand(() => Connect(anyway: false), () => !IsConnected && SelectedClient is not null);
        ConnectAnywayCommand = new RelayCommand(() => Connect(anyway: true), () => CanConnectAnyway);
        DisconnectCommand = new RelayCommand(() => Disconnect(), () => IsConnected);
        ToggleConnectionCommand = new RelayCommand(() => ConnectionExpanded = !ConnectionExpanded);
        StartCommand = new RelayCommand(Start, () => IsConnected && !IsRunning && !IsStopping);
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        CancelLaunchCommand = new RelayCommand(CancelLaunch, () => IsLaunching);
        LaunchOwnCommand = new RelayCommand(LaunchOwn, () => CanOpenCharacters && _wanted is not null);

        _log.Info("Запущено");
    }

    /// <summary>
    /// Первое обновление списка клиентов — отдельно от конструктора: окно зовёт это по Loaded, когда оно уже показано.
    /// Вкладка из прошлого запуска (tabs.json) и клиент её персонажа запущен и свободен — подключиться к нему (бот не
    /// стартует); иначе только заполнить список.
    /// </summary>
    public void StartUp()
    {
        RefreshClients();
        if (_wanted is { } nick && SelectedClient is { } client && client.Nick == nick && !ClientLock.IsTaken(client.Pid))
        {
            _connectionLog.Info($"{nick}: вкладка из прошлого запуска — подключаюсь");
            Connect(anyway: false);
        }
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
            OnPropertyChanged(nameof(CanStashToBank));
            OnPropertyChanged(nameof(CanRepairFromBag));
            OnPropertyChanged(nameof(Summary));
            OnPropertyChanged(nameof(Remembered));
            Chat.Refresh();
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
            OnPropertyChanged(nameof(CanOpenCharacters));
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

    private string? _nick;

    /// <summary>Ник подключённого персонажа (надпись вкладки); null — не подключены или ник ещё не знаем.</summary>
    public string? Nick
    {
        get => _nick;
        private set
        {
            if (!SetProperty(ref _nick, value))
                return;
            if (value is not null)
                _wanted = value;
            OnPropertyChanged(nameof(TabTitle));
            OnPropertyChanged(nameof(Remembered));
        }
    }

    // Чей это вкладка: последний подключённый здесь персонаж (или из tabs.json) — его клиент выбирается и после отключения
    private string? _wanted;

    /// <summary>Надпись на вкладке окна: ник подключённого, иначе — чья это вкладка, иначе «Новый персонаж».</summary>
    public string TabTitle => Nick ?? _wanted ?? "Новый персонаж";

    /// <summary>Что запомнить о вкладке до следующего запуска (tabs.json): сервер и персонаж.</summary>
    public OpenTab Remembered => new(_profile.Id, Nick ?? _wanted);

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
        is ConnectVerdict.NotPatched or ConnectVerdict.OutdatedPatch or ConnectVerdict.UnknownBuild
        or ConnectVerdict.ProtectorChanged or ConnectVerdict.ProtectorUnknown;

    private void ShowHint(ConnectVerdict? verdict, string text)
    {
        _hintVerdict = verdict;
        ConnectHint = text;
        OnPropertyChanged(nameof(CanConnectAnyway));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearHint() => ShowHint(null, "");

    // ── Персонажи: запуск клиента с автовходом ──────────────────────────────

    // Клиент грузится и входит до минуты; после — ещё до двух нажатий Enter на выборе персонажа
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(90);

    /// <summary>Модель окна «Персонажи»: новая на каждое открытие (список читается с диска заново).</summary>
    public CharactersPanel CreateCharacters(IDialogs dialogs)
        => new(_book, Servers, GamePathFor, dialogs, _connectionLog, OpenCharacter);

    private string? GamePathFor(string server) => _config.App.Connection.ClientPaths.TryGetValue(server, out var path) ? path : null;

    private bool _isLaunching;

    /// <summary>Идёт запуск клиента из «Персонажей»: второй не начинаем, пока этот не вошёл или не сдался.</summary>
    public bool IsLaunching
    {
        get => _isLaunching;
        private set
        {
            if (SetProperty(ref _isLaunching, value))
                OnPropertyChanged(nameof(CanOpenCharacters));
        }
    }

    public bool CanOpenCharacters => !IsConnected && !IsLaunching;

    private string _launchText = "";

    /// <summary>Как идёт запуск: «запускаю», «жду персонажа: 20 с», «жму Enter».</summary>
    public string LaunchText
    {
        get => _launchText;
        private set => SetProperty(ref _launchText, value);
    }

    /// <summary>
    /// «Открыть» в «Персонажах»: сервер персонажа → он уже в игре — подключаемся к нему, нет — запускаем клиент с логином,
    /// паролем и ником, ждём персонажа в мире (в фоне) и подключаемся. Бот не запускается — только подключение.
    /// </summary>
    private void OpenCharacter(CharacterAccount account)
    {
        if (!CanOpenCharacters)
            return;

        if (Servers.FirstOrDefault(s => s.Id == account.Server) is not { } profile)
        {
            ShowHint(null, $"Сервера {account.Server} нет в этой версии бота.");
            return;
        }

        ChooseServer(profile);
        if (Clients.FirstOrDefault(c => c.Nick == account.Nick) is { } running)
        {
            if (ClientLock.IsTaken(running.Pid))
            {
                ShowHint(null, $"{account.Nick} уже в игре, и к нему подключено другое окно бота.");
                return;
            }

            _connectionLog.Info($"{account.Nick} уже в игре (PID {running.Pid}) — подключаюсь");
            SelectedClient = running;
            Connect(anyway: false);
            return;
        }

        Launch(account, profile, resume: null);
    }

    /// <summary>
    /// ПКМ по вкладке → «Запустить клиент с персонажем»: персонаж вкладки ищется в «Персонажах» (тот же сервер и ник) и
    /// открывается, как кнопкой «Открыть» там: уже в игре — подключаемся, нет — запуск клиента с входом.
    /// </summary>
    private void LaunchOwn()
    {
        if (_wanted is not { } nick || !CanOpenCharacters)
            return;

        var account = _book.Load(out _).FirstOrDefault(a => a.Is(_profile.Id, nick));
        if (account is null || account.Login.Trim().Length == 0)
        {
            ShowHint(null, $"{nick} не записан в «Персонажах» с логином — добавьте его туда, чтобы запускать клиент.");
            return;
        }

        OpenCharacter(account);
    }

    /// <summary>
    /// Запустить клиент персонажа и войти (в фоне). <paramref name="resume"/> — переподключение: сначала закрыть выпавший
    /// клиент, после входа снова «Старт» с той же точки маршрута.
    /// </summary>
    private void Launch(CharacterAccount account, ServerProfile profile, Resume? resume)
    {
        // Профиль знает окна клиента — бот входит сам, по этапам; нет — клиент входит по логину и паролю в аргументах
        var steps = LoginScreen.Supported(profile.Data);
        ClientLaunch launch;
        string password;
        try
        {
            launch = account.Launch(profile.Data.LaunchArguments, Patch.GamePath, credentials: !steps);
            password = account.PlainPassword();
        }
        catch (InvalidOperationException e)
        {
            LaunchFailed(e.Message);
            return;
        }

        ClearHint();
        IsLaunching = true;
        _resume = resume;
        LaunchText = resume is null ? $"{account.Nick}: запускаю клиент…" : $"{account.Nick}: переподключаюсь…";
        // Пароль в лог не попадает: ToString его не показывает
        _connectionLog.Info($"Запуск {account.Nick}{(steps ? " (вход по этапам)" : "")}: {launch}");
        var cancel = _launchCancel = new CancellationTokenSource();
        Task.Run(() =>
        {
            if (resume is not null)
            {
                _connectionLog.Info($"{account.Nick}: закрываю клиент PID {resume.Pid}");
                ClientLoginAttempt.Close(resume.Pid);
            }

            if (steps)
                LoginBySteps(account, profile, launch, password, cancel.Token);
            else
                Login(account, profile, launch);
        });
    }

    /// <summary>Переподключение: какой клиент закрыть и с какой точки маршрута продолжить.</summary>
    private sealed record Resume(int Pid, int? RouteIndex);

    // Идёт переподключение: после входа — снова «Старт»
    private Resume? _resume;

    /// <summary>
    /// Персонаж выпал из игры, пока бот работал (после «Стопа» сторож не смотрит — переподключения нет). Персонаж есть в
    /// «Персонажах» с логином — остановить бота, закрыть клиент, войти заново и снова «Старт». Нет — только остановить:
    /// клиент остаётся, как есть.
    /// </summary>
    private void LinkLost(ClientConnection connection, string reason)
    {
        if (_connection != connection || !IsRunning)
            return;

        var nick = Nick;
        var profile = _profile;
        _log.Warning(Capitalize(reason));
        var account = nick is null ? null : _book.Load(out _).FirstOrDefault(a => a.Is(profile.Id, nick));
        if (account is null || account.Login.Trim().Length == 0)
        {
            _log.Warning($"Переподключиться нечем: {nick ?? "персонаж"} не записан в «Персонажах» с логином — бот остановлен");
            Stop();
            return;
        }

        var resume = new Resume(connection.Pid, connection.Bot?.Brain.Part<IRouteProgress>()?.Index);
        Disconnect();
        _log.Info($"Переподключаю {account.Nick}");
        BotState = "Переподключение…";
        Launch(account, profile, resume);
    }

    private CancellationTokenSource? _launchCancel;

    private void CancelLaunch()
    {
        _launchCancel?.Cancel();
        LaunchText = "останавливаю вход…";
    }

    // Не в потоке окна: вход с повторами — до десятка минут
    private void LoginBySteps(CharacterAccount account, ServerProfile profile, ClientLaunch launch, string password, CancellationToken token)
    {
        LoginRun run;
        try
        {
            var runner = new LoginRunner(() =>
            {
                using var process = launch.Start();
                _connectionLog.Info($"{account.Nick}: клиент запущен, PID {process.Id}");
                return process.Id;
            }, pid => new ClientLoginAttempt(pid, profile.Data), pid =>
            {
                _connectionLog.Warning($"{account.Nick}: закрываю зависший клиент PID {pid}");
                ClientLoginAttempt.Close(pid);
            });
            run = runner.Run(account.Login.Trim(), password, account.Nick, text =>
            {
                _connectionLog.Info($"{account.Nick}: {text}");
                UiThread.Run(() => LaunchText = $"{account.Nick}: {text}");
            }, token);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            UiThread.Run(() => LaunchFailed($"Не удалось запустить клиент {account.Nick}: {e.Message}"));
            return;
        }

        UiThread.Run(() =>
        {
            if (run.Result is { Ok: true, Nick: { } nick } && run.Pid is { } pid)
                Entered(account, profile, launch.ExePath, pid, nick);
            else if (run.Result.Outcome == LoginOutcome.Cancelled)
                LaunchFailed($"Вход {account.Nick} остановлен.");
            else
                LaunchFailed($"{account.Nick} не вошёл: {run.Result.Text}");
        });
    }

    // Не в потоке окна: вход — до пары минут ожидания
    private void Login(CharacterAccount account, ServerProfile profile, ClientLaunch launch)
    {
        int pid;
        LoginWait result;
        try
        {
            using var process = launch.Start();
            pid = process.Id;
            result = ClientLogin.EnterWorld(pid, profile.Data, LoginTimeout,
                progress: text => UiThread.Run(() => LaunchText = $"{account.Nick}: {text}"));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            UiThread.Run(() => LaunchFailed($"Не удалось запустить клиент {account.Nick}: {e.Message}"));
            return;
        }

        UiThread.Run(() => LoggedIn(account, profile, launch, pid, result));
    }

    private void LoggedIn(CharacterAccount account, ServerProfile profile, ClientLaunch launch, int pid, LoginWait result)
    {
        if (result.State != LoginState.InWorld)
        {
            if (result.Problem is { } problem)
                _connectionLog.Info($"Последняя ошибка чтения при входе: {problem}");
            LaunchFailed(result.State == LoginState.Exited ? $"Клиент {account.Nick} закрылся, не дойдя до игры."
                : !result.WindowFound ? $"Окно клиента {account.Nick} не появилось."
                : $"{account.Nick} не вошёл в игру за {result.Elapsed.TotalSeconds:0} с — посмотрите окно игры (вход или выбор персонажа).");
            return;
        }

        _connectionLog.Info($"{result.Nick} в игре ({result.Elapsed.TotalSeconds:0} с)");
        Entered(account, profile, launch.ExePath, pid, result.Nick!);
    }

    /// <summary>Персонаж в мире — подключить к нему окно бота.</summary>
    private void Entered(CharacterAccount account, ServerProfile profile, string exePath, int pid, string nick)
    {
        var resume = _resume;
        _resume = null;
        IsLaunching = false;
        LaunchText = "";
        _connectionLog.Info($"{nick} в игре (PID {pid})");
        if (nick != account.Nick)
            _connectionLog.Warning($"Вошёл {nick}, а в «Персонажах» записан {account.Nick} — проверьте ник");
        // Пока ждали, окно подключили к другому клиенту — его не трогаем
        if (IsConnected)
        {
            _connectionLog.Info($"Окно уже подключено — {nick} можно подключить в другом окне бота");
            return;
        }

        ChooseServer(profile);
        if (Clients.FirstOrDefault(c => c.Pid == pid) is not { } client)
        {
            // Клиент персонажа — не «Игра» сервера: в списке его нет, добавляем сами
            client = new GameClient(pid, nick, NativeWindows.FindMainWindow(pid, includeHidden: false), exePath);
            Clients.Add(client);
        }

        SelectedClient = client;
        Connect(anyway: false);
        if (resume is not null && IsConnected)
        {
            _log.Info($"{nick} снова в игре — продолжаю");
            Start(CallTransport.Window, resume.RouteIndex);
        }
    }

    private void LaunchFailed(string text)
    {
        if (_resume is not null)
            BotState = "Ожидание";
        _resume = null;
        IsLaunching = false;
        LaunchText = "";
        _connectionLog.Warning(text);
        ConnectionExpanded = true;
        ShowHint(null, text);
    }

    /// <summary>Сервер персонажа и свежий список его клиентов.</summary>
    private void ChooseServer(ServerProfile profile)
    {
        if (_profile != profile)
            SelectedServer = profile;
        else
            ReloadClients();
    }

    // Что бот уже записал в «Персонажи» про подключённого: сервер, ник, класс, уровень
    private string? _seen;

    /// <summary>Подключённый персонаж есть в «Персонажах» — запомнить класс и уровень (в фоне: файл общий, под замком).</summary>
    private void NoteSeen(HostState host)
    {
        if (host.Level <= 0)
            return;

        var (server, nick, @class, level) = (_profile.Id, host.Name, host.Class, host.Level);
        var key = $"{server}|{nick}|{@class}|{level}";
        if (key == _seen)
            return;

        _seen = key;
        Task.Run(() =>
        {
            try
            {
                _book.Seen(server, nick, @class, level, DateTime.Now);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _connectionLog.Warning($"Не удалось записать уровень {nick} в «Персонажи»: {e.Message}");
            }
        });
    }

    // ── Игра и патч (панель: путь, состояние, кнопки) ───────────────────────

    /// <summary>Строки «Игра» и «Патч»: exe сервера, состояние файла, кнопки «Выбрать…», «Пропатчить», «Откат».</summary>
    public ClientPatchPanel Patch { get; }

    /// <summary>Версия бота (Directory.Build.props) — в шапке окна: по ней видно, какой релиз запущен.</summary>
    public string Version { get; } = typeof(CharacterTab).Assembly.GetName().Version?.ToString(3) ?? "";

    // ── Состояние ────────────────────────────────────────────────────────────

    /// <summary>Персонаж, цель, пет — на вкладке «Бот».</summary>
    public StatusPanel Status { get; } = new();

    /// <summary>Статистика персонажа: убито, добыто, смерти, опыт — за сессию, день, неделю, месяц.</summary>
    public StatsPanel Stats { get; } = new();

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

    /// <summary>Остановить вход из «Персонажей» (клиент, который бот запустил, остаётся открытым).</summary>
    public ICommand CancelLaunchCommand { get; }
    public ICommand LaunchOwnCommand { get; }

    private bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            SetProperty(ref _isRunning, value);
            // Уведомления — только пока бот запущен
            if (_notifier is not null)
                _notifier.Active = value;
        }
    }

    private bool _isStopping;

    /// <summary>Бот останавливается: дожидается своего хода (вызов в игру — до 5 с). «Старт» пока нельзя.</summary>
    public bool IsStopping
    {
        get => _isStopping;
        private set => SetProperty(ref _isStopping, value);
    }

    public void Start() => Start(CallTransport.Window);

    /// <param name="routeStart">Обход: с какой точки (переподключение — где был); null — выбранная в списке (не выбрана — первая).</param>
    private void Start(CallTransport transport, int? routeStart = null)
    {
        if (IsRunning || IsStopping || _connection is null)
            return;

        // Смерть на фарме — в Telegram: её замечает статистика мозга, поэтому уведомление стоит перед книгой статистики
        var stats = new DeathNotifier(Stats.Book, () => _config.App.ChatNotify, () => Nick, () => DeathNotifier.Revives(Settings), Notify);
        var result = _connection.StartBot(Settings.Mode, Settings, routeStart ?? Route.StartIndex, transport, stats);
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
    public PotionsPanel Potions { get; }

    /// <summary>Карточка «Ремонт»: у какого NPC чинить.</summary>
    public RepairPanel Repair { get; }

    // ── Маршрут обхода (режим «Собирать ресурсы») ─────────────────────────────

    /// <summary>Вкладка «Ресы»: точки маршрута, что копать, опасные мобы.</summary>
    public RoutePanel Route { get; }

    /// <summary>Вкладка «Кликер»: сценарий — шаги по кругу, у каждого действие и условие.</summary>
    public ClickerPanel Clicker { get; }

    /// <summary>Вкладка «Задания» режима «Прокачать друида».</summary>
    public QuestsPanel Quests { get; }

    /// <summary>Вкладка «Чат»: уведомления в Telegram.</summary>
    public ChatPanel Chat { get; }

    /// <summary>Галочка «Пет только на время боя» — только у сервера, где найден отзыв пета.</summary>
    public bool CanRecallPet => _profile.Capabilities.Has(Capability.RecallPet);

    /// <summary>«Убирать в банк» — только у сервера, где банк открывается из сумки (Comeback).</summary>
    public bool CanStashToBank => _profile.Capabilities.Has(Capability.RemoteBank);

    /// <summary>«Чинить из сумки» — только у сервера, где лавка открывается кнопкой в сумке (Comeback).</summary>
    public bool CanRepairFromBag => _profile.Capabilities.Has(Capability.RemoteRepair);

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

    /// <summary>
    /// Двойной щелчок по вкладке: окно игры её персонажа — сразу под окно бота (<paramref name="botWindow"/>), фокус
    /// остаётся у бота. Окно ищем сейчас, по PID подключения (после переподключения клиент другой); не подключены —
    /// клиента, выбранного в списке.
    /// </summary>
    public void ShowGameWindow(IntPtr botWindow)
    {
        if ((_connection?.Pid ?? SelectedClient?.Pid) is not { } pid)
        {
            _connectionLog.Info("Окно игры: вкладка не подключена и клиент не выбран");
            return;
        }

        var window = NativeWindows.FindMainWindow(pid, includeHidden: true);
        if (window == IntPtr.Zero)
            _connectionLog.Warning($"Окно игры PID {pid} не найдено");
        else if (!NativeWindows.IsVisible(window))
            _connectionLog.Info($"Окно игры PID {pid} скрыто в трей — откройте его значком в трее");
        else if (!NativeWindows.PlaceBelow(window, botWindow))
            _connectionLog.Info($"Окно игры PID {pid}: Windows не дала поставить его под окно бота");
    }

    /// <summary>Лог «Чат» вкладки — туда же пишут команды из Telegram.</summary>
    internal ILogger ChatLog => _chatLog;

    // Когда пришёл последний снимок мира: нет свежего — персонаж не в мире (экран входа, разрыв), шептать нельзя
    private DateTime _lastWorldAt;
    private static readonly TimeSpan FreshWorld = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Шепнуть от этого персонажа (команда из Telegram) — в фоне: вызов в игру ждёт до 5 с. Бот может и стоять — нужно только
    /// подключение и персонаж в мире. Задача — null, если отправлено, иначе причина.
    /// </summary>
    public async Task<string?> WhisperAsync(string nick, string text)
    {
        if (_connection is not { } connection)
            return "вкладка не подключена";
        if (_lastWorld is null || DateTime.UtcNow - _lastWorldAt > FreshWorld)
            return "персонаж не в мире";

        var result = await Task.Run(() => connection.Whisper(nick, text));
        if (result.Ok)
            _chatLog.Info($"Шёпот из Telegram — {nick}: {text}");
        else
            _chatLog.Warning($"Шёпот из Telegram {nick} не отправлен: {result.Details}");
        return result.Ok ? null : result.Details;
    }

    /// <summary>
    /// Закрыть вкладку: настройки и точки — на диск, вход из «Персонажей» — прервать, отключиться. Задача — когда подключение
    /// закрыто (бот работал — до 7 с: окно закрывает все вкладки разом и ждёт их вместе).
    /// </summary>
    public Task CloseAsync()
    {
        FlushSettings();
        _launchCancel?.Cancel();
        var closing = Disconnect();
        _spots.Save();
        Route.Dispose();
        _appSettings.Saved -= AppSettingsSaved;
        return closing;
    }

    // Общие настройки сохранила другая вкладка — показать её правку (уведомления, переименование окон, фильтр лога)
    private void AppSettingsSaved(object? by)
    {
        if (by == this)
            return;
        Chat.Reload();
        Log.Reload(_config.App.LogView);
        OnPropertyChanged(nameof(RenameWindows));
    }

    public void Dispose() => CloseAsync().Wait(ClientConnection.StopWait + TimeSpan.FromSeconds(1));

    // Сообщение из чата — в Telegram (в фоне, разметка HTML: ники копируются нажатием) и в лог
    private void Notify(string html)
    {
        var s = _config.App.ChatNotify;
        _chatLog.Info($"В Telegram: {TelegramCommands.Plain(html).Replace('\n', ' ')}");
        _telegram.Post(s.Token, s.ChatId, html, _chatLog, html: true);
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
        var game = Patch.GamePath;
        var clients = ClientList.Build(source, processes, game);
        if (game is null && ClientList.GuessGame(clients) is { } guessed)
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
            SelectedClient = ClientList.KeepSelection(clients, keep, _wanted ?? _config.App.Connection.LastCharacter, ClientLock.IsTaken);
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
        if (alive && Patch.GamePath is null && client.ExePath is { } path)
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
            var connection = _connection = ClientConnection.Open(client.Pid, _profile.Data, _logger, _logsDirectory);
            _spots.Server = _profile.Id;
            // Блокноту ресурсов, логу урона и «кто убил» — каждый снимок, прямо в потоке снимков; окну — только последний
            connection.WorldUpdated += _spots.Observe;
            connection.WorldUpdated += new DamageLog(_logger.For(LogSources.Damage)).Observe;
            // Запись мира вокруг смерти — первой: «кто убил» отмечает событие снимком, который она уже видела
            connection.WorldUpdated += _incidents.Observe;
            var killer = new KillerLog(_logger.For(LogSources.Brain), _profile.Data.Chat.KilledBy, _profile.Data.Chat.PetDied);
            killer.Noted += _incidents.Mark;
            connection.WorldUpdated += killer.Observe;
            _notifier = new ChatNotifier(_profile.Data.Chat.Channels, () => _config.App.ChatNotify, Notify) { Active = IsRunning };
            connection.WorldUpdated += _notifier.Observe;
            // Окну — только от текущего подключения: прошлое ещё может гаснуть в фоне (см. Disconnect)
            connection.WorldUpdated += world =>
            {
                if (_connection == connection)
                    ShowLatest(world);
            };
            connection.WorldFailed += message => UiThread.Run(() =>
            {
                if (_connection == connection)
                    ShowFailure(message);
            });
            connection.BotStatusChanged += status => UiThread.Run(() => BotState = Capitalize(status));
            // Из хода бота — остановку не здесь, а в потоке окна (бот ждёт как раз этот ход)
            connection.BotStopRequested += _ => UiThread.Run(Stop);
            connection.LinkLost += reason => UiThread.Run(() => LinkLost(connection, reason));

            IsConnected = true;
            ConnectionText = client.Nick ?? $"PID {client.Pid}";
            Nick = client.Nick;
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
    /// Отключиться. Подключение сначала останавливает бота и ждёт его ход (до 7 с): бот работал или ещё останавливается — ждём
    /// в фоне, чтобы не замерло окно со всеми вкладками; вкладка сразу «не подключена», события гаснущего подключения она
    /// пропускает. Бот стоит — подключение закрывается сразу. Задача — когда подключение закрыто.
    /// </summary>
    private Task Disconnect()
    {
        var wasRunning = IsRunning;
        IsRunning = false;
        var closing = Close(_connection);
        _connection = null;
        _notifier = null;
        _incidents.Flush();
        Interlocked.Exchange(ref _latest, null);
        if (wasRunning)
            _log.Info("Стоп");
        IsStopping = false;
        BotState = "Ожидание";
        IsConnected = false;
        ConnectionText = "";
        Nick = null;
        ConnectionExpanded = true;
        ClearWorld();
        return closing;
    }

    private Task Close(ClientConnection? connection)
    {
        if (connection is null)
            return Task.CompletedTask;
        if (connection.Bot is null && !connection.IsStopping)
        {
            connection.Dispose();
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                connection.Dispose();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                _connectionLog.Error($"Отключение: {e.Message}");
            }
        });
    }

    // Файл — не в потоке снимков: текст случая собирается из сотен снимков
    private void SaveIncident(Incident incident) => Task.Run(() =>
    {
        var log = _logger.For(LogSources.Brain);
        try
        {
            var path = new IncidentFiles(Path.Combine(_logsDirectory, IncidentFiles.FolderName)).Write(incident);
            log.Info($"Что было вокруг: {path}");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            log.Error($"Не удалось записать, что было вокруг: {e.Message}");
        }
    });

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
        _lastWorldAt = DateTime.UtcNow;
        // «Задания»: варианты списка «не брать» — из «Доступных», их читаем, пока выбран этот режим. Ушли из режима — не читаем,
        // кроме запущенного бота: он сам просит задания каждый тик, пока работает
        if (Settings.Mode == BotMode.LevelUp)
            _connection.Requests.Quests = true;
        else if (_connection.Bot is null)
            _connection.Requests.Quests = false;
        Pets.Observe(w);
        Potions.Observe(w, _connection.Bot?.Brain.Part<IPotionFallback>());
        Repair.Observe(w);
        Farm.Observe(w);
        var h = w.Host;
        if (h.Name.Length > 0)
        {
            SwitchCharacter(h.Name);
            Nick = h.Name;
            NoteSeen(h);
        }
        ConnectionText = h.Name;
        Status.Show(w);
        Farm.ShowFor(w);
        Route.ShowDistances(w);
        Clicker.ShowCurrent();
    }

    private void ShowFailure(string message)
    {
        if (_connection is { HasExited: true } connection)
        {
            // Бот работал — переподключиться (сторож увидел бы то же самое через пару секунд)
            if (IsRunning)
            {
                LinkLost(connection, "клиент игры закрылся");
                return;
            }

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
        if (nick == _config.Character && _profile.Id == _config.Server)
            return;

        // Правки прошлого персонажа — в его файл, пока он ещё текущий
        FlushSettings();
        Settings.Edited -= SettingsEdited;
        _config.SwitchTo(_profile.Id, nick);
        _fileLog.SetCharacter(_profile.Id, nick);
        Settings.Edited += SettingsEdited;
        OpenStats(nick);

        _connection?.Bot?.UpdateSettings(Settings);
        OnPropertyChanged(nameof(Settings));
        Pets.Rebind();
        Potions.Rebind();
        Repair.Rebind();
        Farm.Load();
        Route.Load();
        Clicker.Load();
        Quests.Load();
        ModeChanged();
        OnPropertyChanged(nameof(SettingsOwner));
    }

    // Статистика персонажа: старые месяцы дочитываются в фоне, бот может писать сразу
    private void OpenStats(string nick)
    {
        var book = new StatsBook(StatsBook.DirectoryFor(_appDirectory, _profile.Id, nick), DateTime.Now, _log);
        Stats.SetBook(book, moneyKnown: _profile.Data.Host.Money != 0);
        Task.Run(book.Load);
    }

    private WorldState? _lastWorld;

    public sealed record ModeChoice(BotMode Mode, string Title);

    /// <summary>
    /// Режимы в окне. «Прокачать друида» (<see cref="BotMode.LevelUp"/>) не доделан — скрыт, чтобы не попал в релиз; сохранённый у
    /// персонажа заменяется на «Бить мобов» (<see cref="BotSettings.Normalize"/>). Вернуть — добавить сюда и убрать замену там.
    /// </summary>
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
    public bool ShowQuestsTab => Settings.Mode == BotMode.LevelUp;

    private void ModeChanged()
    {
        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(ShowResourcesTab));
        OnPropertyChanged(nameof(ShowClickerTab));
        OnPropertyChanged(nameof(ShowMobsTab));
        OnPropertyChanged(nameof(ShowQuestsTab));
        // Вкладка исчезла — на «Бот»
        if ((Tab == TabResources && !ShowResourcesTab) || (Tab == TabClicker && !ShowClickerTab) || (Tab == TabMobs && !ShowMobsTab)
            || (Tab == TabQuests && !ShowQuestsTab))
            Tab = 0;
    }

    public const int TabMobs = 1;
    public const int TabResources = 3;
    public const int TabClicker = 4;
    public const int TabQuests = 6;

    /// <summary>Чьи настройки сейчас на вкладках «Мобы» и «Общее».</summary>
    public string SettingsOwner => _config.Character is null ? "Общие настройки (персонаж не выбран)" : $"Настройки персонажа {_config.Character}";
}

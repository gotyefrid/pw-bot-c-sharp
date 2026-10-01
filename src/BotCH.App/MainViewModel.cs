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
    private static readonly TimeSpan SnapshotPeriod = TimeSpan.FromMilliseconds(250);

    private readonly ProfileCatalog _catalog = ProfileCatalog.Default();
    private readonly SettingsStore _store;
    private readonly BotSettings _settings;
    private readonly ILogger _log;
    private readonly ILogger _connectionLog;
    private readonly Logger _logger;

    private GameProcess? _game;
    private WorldMonitor? _monitor;
    private Unfreezer? _unfreezer;
    private SkillNames _skillNames = SkillNames.Empty;
    private IServerProfile _profile;
    private bool _refreshing;

    // Бот (мозг) — только пока нажат «Старт». Вызовы в игре — отдельным дескриптором с правом Execute
    private GameProcess? _exec;
    private BotBrain? _brain;
    private Action<WorldState>? _brainTick;

    public MainViewModel(string appDirectory)
    {
        var ring = new RingBufferSink(MaxLogLines);
        var logger = new Logger().AddSink(ring).AddSink(new DailyFileSink(Path.Combine(appDirectory, "logs")));
        ring.Added += entry => OnUi(() => AddLog(entry));
        _logger = logger;
        _log = logger.For("окно");
        _connectionLog = logger.For("подключение");

        _store = new SettingsStore(Path.Combine(appDirectory, "settings.json"));
        _settings = _store.Load(out var problem);
        if (problem is not null)
            _log.Warning(problem);

        Servers = _catalog.Ids.Select(id => _catalog.Load(id)).ToList();
        _profile = Servers.FirstOrDefault(s => s.Id == _settings.Connection.ServerId) ?? Servers.First();

        RefreshCommand = new RelayCommand(RefreshClients);
        StartCommand = new RelayCommand(Start, () => IsConnected && !IsRunning);
        StopCommand = new RelayCommand(Stop, () => IsRunning);

        ClearLogCommand = new RelayCommand(Log.Clear);

        _log.Info("BotCH запущен");
        RefreshClients();
    }

    // ── Подключение ──────────────────────────────────────────────────────────

    public IReadOnlyList<IServerProfile> Servers { get; }

    public IServerProfile SelectedServer
    {
        get => _profile;
        set
        {
            if (value is null || !SetProperty(ref _profile, value))
                return;

            _settings.Connection.ServerId = value.Id;
            SaveSettings();
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
            var previous = _selectedClient?.Pid;
            if (!SetProperty(ref _selectedClient, value) || _refreshing || value?.Pid == previous)
                return;

            Connect(value);
        }
    }

    public bool RenameWindows
    {
        get => _settings.Connection.RenameWindows;
        set
        {
            if (_settings.Connection.RenameWindows == value)
                return;

            _settings.Connection.RenameWindows = value;
            OnPropertyChanged();
            SaveSettings();
            if (value)
                RenameAll();
        }
    }

    public bool Unfreeze
    {
        get => _settings.Connection.Unfreeze;
        set
        {
            if (_settings.Connection.Unfreeze == value)
                return;

            _settings.Connection.Unfreeze = value;
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

    /// <summary>Горячие клавиши: окно сообщает, удалось ли их занять.</summary>
    public void ReportHotKeys(bool start, bool stop)
    {
        if (start && stop)
            _log.Info("Горячие клавиши: Ctrl+Alt+Num1 — старт, Ctrl+Alt+Num0 — стоп");
        else
            _log.Warning("Горячие клавиши заняты другой программой (запущен старый бот?): "
                + (start ? "" : "Ctrl+Alt+Num1 ") + (stop ? "" : "Ctrl+Alt+Num0") + ". Кнопки в окне работают");
    }

    private bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public void Start()
    {
        if (IsRunning || _game is null || _monitor is null)
            return;

        try
        {
            _exec = GameProcess.Open(_game.Pid, GameProcessRights.Execute);
            var caller = new GameCaller(_game, _exec, _game.MainModuleBase, _profile.Data);
            foreach (var function in caller.Functions.Where(f => !f.IsUsable))
                _log.Warning($"Функция {function.Name} недоступна: {function.Details}");

            var runner = new ActionRunner(new DirectCallActions(caller), _logger.For("действия"));
            _brain = new BotBrain(runner, _profile.Data.Skills, _settings, _logger.For("мозг"));
            _brain.StatusChanged += status => OnUi(() => BotState = Capitalize(status));
            _brain.StopRequested += reason => OnUi(Stop);
            _brainTick = _brain.Tick;
            _monitor.Updated += _brainTick;

            IsRunning = true;
            BotState = "Запуск…";
            _log.Info("Старт");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Error("Не удалось запустить бота: " + e.Message);
            Stop();
        }
    }

    public void Stop()
    {
        if (_brainTick is not null && _monitor is not null)
            _monitor.Updated -= _brainTick;
        _brainTick = null;
        _brain?.Reset();
        _brain = null;
        _exec?.Dispose();
        _exec = null;

        if (IsRunning)
            _log.Info("Стоп");
        IsRunning = false;
        BotState = "Ожидание";
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpper(text[0]) + text.Substring(1);

    // ── Настройки бота ──────────────────────────────────────────────────────

    private int _tab;

    /// <summary>Вкладка в середине окна: 0 — бот, 1 — настройки, 2 — лог.</summary>
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
    public BotSettings Settings => _settings;

    public sealed record SkillChoice(int Id, string Title);

    /// <summary>Атакующие скиллы персонажа (изученные, кроме лечения/воскрешения/портала) с названиями из игры.</summary>
    public ObservableCollection<SkillChoice> AttackSkills { get; } = new();

    public string MobNamesText
    {
        get => string.Join(", ", _settings.Target.MobNames);
        set
        {
            _settings.Target.MobNames = MobNameFilter.Clean(value.Split(','));
            SettingsEdited();
        }
    }

    public string LootNamesText
    {
        get => string.Join(", ", _settings.Loot.ItemNames);
        set
        {
            _settings.Loot.ItemNames = MobNameFilter.Clean(value.Split(','));
            SettingsEdited();
        }
    }

    public IReadOnlyList<LootListMode> LootModes { get; } = [LootListMode.All, LootListMode.OnlyListed, LootListMode.ExceptListed];

    /// <summary>Любая правка настройки: сохранить файл и отдать копию работающему боту.</summary>
    public void SettingsEdited()
    {
        SaveSettings();
        _brain?.UpdateSettings(_settings);
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
        if (ids.Count > 0 && !ids.Contains(_settings.Combat.AttackSkillId))
        {
            _settings.Combat.AttackSkillId = ids.Contains(_profile.Data.Skills.DefaultAttack) ? _profile.Data.Skills.DefaultAttack : ids[0];
            SettingsEdited();
        }

        OnPropertyChanged(nameof(Settings));
    }

    /// <summary>Сообщение об ошибке из окна — в лог.</summary>
    public void ReportProblem(string message) => _log.Warning(message);

    public void Dispose()
    {
        Disconnect();
    }

    // ── Внутреннее ──────────────────────────────────────────────────────────

    private void RefreshClients()
    {
        var keep = _selectedClient?.Pid;
        var clients = ClientList.Build(new SystemClientSource(_profile.Data), _profile.Data.ClientProcessName);

        _refreshing = true;
        try
        {
            Clients.Clear();
            foreach (var client in clients)
                Clients.Add(client);
            SelectedClient = ClientList.KeepSelection(clients, keep);
        }
        finally
        {
            _refreshing = false;
        }

        if (clients.Count == 0)
            _connectionLog.Warning("Клиенты игры не найдены — запустите игру и нажмите «обновить»");

        RenameAll();
        if (SelectedClient?.Pid != _game?.Pid || _game is null || _game.HasExited)
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
            _game = GameProcess.Open(client.Pid);
            var reader = new WorldReader(_game, _game.MainModuleBase, _profile.Data, id => _skillNames.Get(id));
            _monitor = new WorldMonitor(reader.Read, SnapshotPeriod);
            _monitor.Updated += state => OnUi(() => Show(state));
            _monitor.Failed += message => OnUi(() => ShowFailure(message));
            _monitor.Start();

            IsConnected = true;
            ConnectionText = client.Nick ?? $"PID {client.Pid}";
            _connectionLog.Info($"Подключено: {client.Display}, сервер {_profile.Name}");
            LoadSkillNamesInBackground(_game.MainModulePath);
            ApplyUnfreeze();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Disconnect();
            ConnectionText = "Ошибка подключения";
            _connectionLog.Error($"Не удалось подключиться к PID {client.Pid}: {e.Message}");
        }
    }

    private void Disconnect()
    {
        Stop();
        _monitor?.Dispose();
        _monitor = null;
        _unfreezer?.Dispose();
        _unfreezer = null;
        _game?.Dispose();
        _game = null;
        IsConnected = false;
    }

    private void LoadSkillNamesInBackground(string clientPath)
    {
        Task.Run(() =>
        {
            var names = SkillNames.LoadFromGameDirectory(Path.GetDirectoryName(clientPath)!, out var problem);
            _skillNames = names;
            if (problem is not null)
                _connectionLog.Warning("Названия скиллов: " + problem);
            else
                _connectionLog.Debug($"Названия скиллов: {names.Count}");
        });
    }

    private void ApplyUnfreeze()
    {
        _unfreezer?.Dispose();
        _unfreezer = null;
        if (!Unfreeze || _game is null)
            return;

        try
        {
            _unfreezer = new Unfreezer(_game.Pid, _profile.Data, message => _connectionLog.Warning(message));
            _unfreezer.Start();
            _connectionLog.Info("Unfreeze включён");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _unfreezer = null;
            _connectionLog.Error("Unfreeze: " + e.Message);
        }
    }

    private void Show(WorldState w)
    {
        if (_monitor is null)
            return;

        var h = w.Host;
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
        TargetDetails = target is null ? "" : $"HP {target.Hp} · {target.Distance:0.0} м · {StateText(target, w)}";

        var pet = w.Pet;
        var active = pet?.ActiveCage is int cage ? pet.InCage(cage) : null;
        HasPet = active is not null;
        PetTitle = pet is null ? "Пета нет" : active is null ? "Пет не призван" : $"Пет · клетка {active.Cage}";
        PetHpPercent = active?.HpPercent ?? 0;
        PetHpText = active is null ? "" : $"{active.HpPercent} %";
        PetDetails = active is null ? "" : active.IsHungry ? "голоден" : "сыт";

        UpdateAttackSkills(w);
        SnapshotInfo = $"мобов {w.Mobs.Count()} · лута {w.GroundItems.Count} · снимок {w.ReadDuration.TotalMilliseconds:0.0} мс";
    }

    private void ShowFailure(string message)
    {
        if (_game is { HasExited: true })
        {
            _connectionLog.Warning("Клиент игры закрыт");
            Disconnect();
            ConnectionText = "Клиент закрыт";
            RefreshClients();
            return;
        }

        ConnectionText = "Персонаж не в мире";
        SnapshotInfo = message;
    }

    private static string StateText(NpcInfo n, WorldState w)
    {
        var state = n.State switch
        {
            1 => "стоит",
            2 => "бьёт",
            3 => "кастует",
            4 => "мёртв",
            5 => "идёт",
            _ => "",
        };
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

    private void SaveSettings()
    {
        try
        {
            _store.Save(_settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error("Не удалось сохранить настройки: " + e.Message);
        }
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}

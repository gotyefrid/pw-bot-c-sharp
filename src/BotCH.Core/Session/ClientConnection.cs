using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Clients;
using BotCH.Core.GameFiles;
using BotCH.Core.Logging;
using BotCH.Core.Memory;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Session;

/// <summary>Чем кончился «Старт».</summary>
public enum StartStatus
{
    Started,

    /// <summary>Основной способ вызовов (через окно игры) не подключился — можно попробовать запасной (<see cref="CallTransport.Thread"/>).</summary>
    WindowFailed,

    /// <summary>Не вышло совсем.</summary>
    Failed,
}

/// <param name="Problem">Причина для пользователя (пусто, если запущен).</param>
public sealed record StartResult(StartStatus Status, string Problem = "");

/// <summary>
/// Подключение к одному клиенту игры: дескриптор чтения, метка клиента, снимки мира, unfreeze, названия скиллов — и текущий
/// бот. Бот принадлежит подключению: запускает и останавливает его только оно (<see cref="StartBot"/>,
/// <see cref="StopBotAsync"/>), окно лишь просит. Закрывается по порядку: бот (дождавшись его хода) → снимки → unfreeze →
/// дескриптор → метка.
/// </summary>
public sealed class ClientConnection : IDisposable
{
    private static readonly TimeSpan SnapshotPeriod = TimeSpan.FromMilliseconds(250);

    /// <summary>Сколько отключение ждёт остановки бота (его ход с вызовом в игру — до 5 с).</summary>
    public static readonly TimeSpan StopWait = TimeSpan.FromSeconds(7);

    private readonly object _lock = new();
    private readonly ProfileData _profile;
    private readonly Logger _logger;
    private readonly ILogger _log;
    private readonly GameProcess _game;
    private readonly ClientLock? _clientLock;
    private readonly WorldMonitor _monitor;
    private Unfreezer? _unfreezer;
    private volatile SkillNames _skillNames = SkillNames.Empty;
    private RunningBot? _bot;
    private Task _stopping = Task.CompletedTask;
    private bool _disposed;

    private ClientConnection(GameProcess game, ClientLock? clientLock, ProfileData profile, Logger logger)
    {
        _game = game;
        _clientLock = clientLock;
        _profile = profile;
        _logger = logger;
        _log = logger.For("подключение");
        if (clientLock is null)
            _log.Warning($"PID {game.Pid} уже подключён в другом окне BotCH — не запускайте двух ботов на один клиент");

        var reader = new WorldReader(game, game.MainModuleBase, profile, id => _skillNames.Get(id));
        _monitor = new WorldMonitor(reader.Read, SnapshotPeriod);
        _monitor.Updated += world => WorldUpdated?.Invoke(world);
        _monitor.Failed += message => WorldFailed?.Invoke(message);
    }

    /// <summary>Открыть клиент для чтения. Снимки пойдут после <see cref="Start"/> — сначала подписаться на события.</summary>
    public static ClientConnection Open(int pid, ProfileData profile, Logger logger)
    {
        var game = GameProcess.Open(pid);
        try
        {
            return new ClientConnection(game, ClientLock.TryTake(pid), profile, logger);
        }
        catch
        {
            game.Dispose();
            throw;
        }
    }

    public int Pid => _game.Pid;

    public bool HasExited => _game.HasExited;

    /// <summary>Снимок мира — из потока снимков.</summary>
    public event Action<WorldState>? WorldUpdated;

    /// <summary>Мир не прочитался — из потока снимков.</summary>
    public event Action<string>? WorldFailed;

    /// <summary>Что делает бот — из потока снимков.</summary>
    public event Action<string>? BotStatusChanged;

    /// <summary>Бот сам просит остановку (погиб, у игры кончается память) — из потока снимков, посреди хода: остановить не здесь.</summary>
    public event Action<string>? BotStopRequested;

    /// <summary>Текущий бот; null — не запущен.</summary>
    public RunningBot? Bot
    {
        get
        {
            lock (_lock)
                return _bot;
        }
    }

    /// <summary>Бот ещё останавливается (ждёт свой ход) — новый «Старт» пока нельзя.</summary>
    public bool IsStopping
    {
        get
        {
            lock (_lock)
                return !_stopping.IsCompleted;
        }
    }

    /// <summary>Начать читать мир и грузить названия скиллов.</summary>
    public void Start()
    {
        _monitor.Start();
        LoadSkillNamesInBackground();
    }

    /// <summary>
    /// Запустить бота. Основной способ вызовов не подключился — <see cref="StartStatus.WindowFailed"/>: спросить пользователя и,
    /// если согласен, позвать ещё раз с <see cref="CallTransport.Thread"/>. Сам на запасной не переходит.
    /// </summary>
    public StartResult StartBot(BotMode mode, BotSettings settings, CallTransport transport)
    {
        lock (_lock)
        {
            if (_bot is not null || !_stopping.IsCompleted)
                return new StartResult(StartStatus.Failed, "бот уже работает или ещё останавливается");
        }

        var calls = GameCalls.Open(_game, _profile, transport, out var problem);
        if (calls is null)
            return new StartResult(transport == CallTransport.Window ? StartStatus.WindowFailed : StartStatus.Failed, problem);

        try
        {
            foreach (var function in calls.Caller.Functions.Where(f => !f.IsUsable))
                _log.Warning($"Функция {function.Name} недоступна: {function.Details}");

            var runner = new ActionRunner(calls.Actions, _logger.For("действия"));
            var brain = BotModes.Create(mode, runner, _profile.Skills, settings, _logger.For("мозг"), _profile.GatherTools);
            var memoryLog = _logger.For("память");
            var guard = new GameMemoryGuard(_game.QueryFreeMemory, memoryLog);
            if (_game.QueryFreeMemory() is FreeMemory free)
                memoryLog.Info($"Свободно у игры {free.TotalMb} МБ, кусок подряд {free.Largest / 1024} КБ");

            var bot = new RunningBot(_monitor, brain, calls, guard.ShouldStop);
            bot.StatusChanged += status => BotStatusChanged?.Invoke(status);
            bot.StopRequested += reason => BotStopRequested?.Invoke(reason);
            lock (_lock)
                _bot = bot;
            bot.Start();
            return new StartResult(StartStatus.Started);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            calls.Dispose();
            return new StartResult(StartStatus.Failed, e.Message);
        }
    }

    /// <summary>
    /// Остановить бота в фоне: он дождётся своего хода (вызов в игру — до 5 с) и закроет вызовы. Повторный вызов, пока
    /// остановка идёт, отдаёт ту же задачу. Не звать из хода бота с ожиданием — бот ждёт этот же ход.
    /// </summary>
    public Task StopBotAsync()
    {
        lock (_lock)
        {
            if (_bot is not { } bot)
                return _stopping;

            _bot = null;
            _stopping = Task.Run(bot.Dispose);
            return _stopping;
        }
    }

    /// <summary>Включить или выключить unfreeze (работа клиента в фоне); что вышло — в лог.</summary>
    public void SetUnfreeze(bool on)
    {
        _unfreezer?.Dispose();
        _unfreezer = null;
        if (!on)
            return;

        try
        {
            var unfreezer = new Unfreezer(_game.Pid, _profile, message => _log.Warning(message));
            if (!unfreezer.IsSupported)
            {
                unfreezer.Dispose();
                _log.Info("Unfreeze на этом сервере не нужен — включите в настройках клиента работу в фоне");
                return;
            }

            unfreezer.Start();
            _unfreezer = unfreezer;
            _log.Info("Unfreeze включён");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _log.Error("Unfreeze: " + e.Message);
        }
    }

    private void LoadSkillNamesInBackground()
    {
        var directory = Path.GetDirectoryName(_game.MainModulePath)!;
        var pck = _profile.GameFiles.Pck;
        Task.Run(() =>
        {
            var names = SkillNames.LoadFromGameDirectory(directory, pck, out var problem);
            _skillNames = names;
            if (problem is not null)
                _log.Warning("Названия скиллов: " + problem);
            else
                _log.Debug($"Названия скиллов: {names.Count}");
        });
    }

    /// <summary>
    /// Отключиться. Сначала бот — ждём его остановку до <see cref="StopWait"/> (окно может замереть, если как раз идёт вызов
    /// в игру). Не дождались — закрываемся дальше: бот закроет свой дескриптор вызовов сам, когда его ход выйдет; закрытие
    /// дескриптора чтения посреди чтения безопасно (SafeHandle держит счётчик ссылок, следующее чтение просто не удастся).
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!StopBotAsync().Wait(StopWait))
            _log.Warning($"Бот не остановился за {StopWait.TotalSeconds:0} с — отключаюсь, он закроет вызовы сам, когда закончит ход");
        _monitor.Dispose();
        _unfreezer?.Dispose();
        _unfreezer = null;
        _game.Dispose();
        _clientLock?.Dispose();
    }
}

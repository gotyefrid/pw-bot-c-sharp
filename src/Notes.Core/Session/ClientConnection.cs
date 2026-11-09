using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Calls;
using Notes.Core.Clients;
using Notes.Core.GameFiles;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.Stats;
using Notes.Core.World;

namespace Notes.Core.Session;

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
/// Подключение к одному клиенту игры: дескриптор чтения, метка клиента, снимки мира, названия и иконки скиллов — и текущий
/// бот. Бот принадлежит подключению: запускает и останавливает его только оно (<see cref="StartBot"/>,
/// <see cref="StopBotAsync"/>), окно лишь просит. Закрывается по порядку: бот (дождавшись его хода) → обработчик окна игры →
/// снимки → дескриптор → метка.
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
    private readonly GameRoots _roots;
    private readonly ClientLock? _clientLock;
    private readonly WorldMonitor _monitor;
    private volatile SkillNames _skillNames = SkillNames.Empty;
    private volatile SkillIcons _skillIcons = SkillIcons.Empty;
    private volatile IconAtlas _macroIcons = IconAtlas.Empty;
    // Строки окон игры (шаблон «Вы говорите с …») — читаются при первом шёпоте
    private volatile UiStrings? _uiStrings;
    private RunningBot? _bot;
    private LinkWatch? _watch;
    private LoginScreen? _screen;
    private Task _stopping = Task.CompletedTask;
    private readonly WorldRequests _requests;

    /// <summary>Что читать в снимок сверх обычного (окно просит задания в режиме «Прокачать друида»).</summary>
    public WorldRequests Requests => _requests;
    private bool _disposed;

    private ClientConnection(GameProcess game, ClientLock? clientLock, ProfileData profile, Logger logger)
    {
        _game = game;
        _clientLock = clientLock;
        _profile = profile;
        _logger = logger;
        _log = logger.For(LogSources.Connection);
        if (clientLock is null)
            _log.Warning($"PID {game.Pid} уже подключён в другом окне — не запускайте двух ботов на один клиент");

        // База ищется в коде клиента — сразу, один раз на подключение
        _roots = GameRoots.Find(game, game.MainModuleBase, profile);
        try
        {
            _log.Info($"База: [модуль + 0x{_roots.Pointer - game.MainModuleBase:X}]");
        }
        catch (MemoryAccessException e)
        {
            _log.Warning(e.Message);
        }
        var reader = new WorldReader(game, _roots, profile, id => _skillNames.Get(id));
        // Читатель — факты из памяти, трекер — то, что требует истории (застрявшая цель, «идёт к нам»)
        var tracker = new NpcTracker();
        _requests = reader.Requests;
        _monitor = new WorldMonitor(() => tracker.Track(reader.Read()), SnapshotPeriod);
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

    /// <summary>
    /// Пока бот работает, персонаж выпал из игры (<see cref="LinkWatch"/>): разрыв связи, экран входа, клиент закрылся — из
    /// потока сторожа, один раз за запуск бота. Бот при этом не останавливается — решает окно.
    /// </summary>
    public event Action<string>? LinkLost;

    /// <summary>Иконки скиллов клиента; пока не загрузились (или их нет) — пусто.</summary>
    public SkillIcons SkillIcons => _skillIcons;

    /// <summary>
    /// Иконка макроса: клетка набора iconlist_skillgrp = номер иконки + 1, как в окне макросов игры (клетки 0 и 1 — значки
    /// «обычная атака» и «повтор» для шагов макроса); null — нет. Проверено: PW Classic, номер 13 — череп на огне, клетка 14.
    /// </summary>
    public IconImage? MacroIcon(MacroInfo macro) => _macroIcons.GetAt(macro.Icon + 1);

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

    /// <summary>Начать читать мир и грузить названия и иконки скиллов.</summary>
    public void Start()
    {
        _monitor.Start();
        LoadSkillsInBackground();
    }

    /// <summary>
    /// Запустить бота. Основной способ вызовов не подключился — <see cref="StartStatus.WindowFailed"/>: спросить пользователя и,
    /// если согласен, позвать ещё раз с <see cref="CallTransport.Thread"/>. Сам на запасной не переходит.
    /// </summary>
    /// <param name="routeStart">Обход: с какой точки начинать (с 0).</param>
    /// <param name="stats">Статистика персонажа: куда бот пишет убийства, добычу, смерти; нет — не ведём.</param>
    public StartResult StartBot(BotMode mode, BotSettings settings, int routeStart, CallTransport transport, IStatsSink? stats = null)
    {
        lock (_lock)
        {
            if (_bot is not null || !_stopping.IsCompleted)
                return new StartResult(StartStatus.Failed, "бот уже работает или ещё останавливается");
        }

        var calls = GameCalls.Open(_game, _roots, _profile, transport, out var problem);
        if (calls is null)
            return new StartResult(transport == CallTransport.Window ? StartStatus.WindowFailed : StartStatus.Failed, problem);

        try
        {
            foreach (var (name, why) in calls.Caller.Problems)
                _log.Warning($"Функция {name} недоступна: {why}");

            var runner = new ActionRunner(new GameControl(calls.Caller, new WindowKeys(_game.Pid), _requests), _logger.For(LogSources.Actions));
            var brain = BotModes.Create(mode, runner, _profile.Skills, settings, _logger.For(LogSources.Brain), _profile.GatherTools, routeStart,
                quests: mode == BotMode.LevelUp ? LoadQuestGuide() : null, stats: stats);
            var memoryLog = _logger.For(LogSources.Memory);
            var inspector = new ProcessInspector(_game);
            var guard = new GameMemoryGuard(inspector.QueryFreeMemory, memoryLog);
            if (inspector.QueryFreeMemory() is FreeMemory free)
                memoryLog.Info($"Свободно у игры {free.TotalMb} МБ, кусок подряд {free.Largest / 1024} КБ");

            var bot = new RunningBot(_monitor, brain, calls, guard.ShouldStop, () => calls.Broken, _log);
            bot.StatusChanged += status => BotStatusChanged?.Invoke(status);
            bot.StopRequested += reason => BotStopRequested?.Invoke(reason);
            lock (_lock)
                _bot = bot;
            bot.Start();
            WatchLink();
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
            _watch?.Dispose();
            _watch = null;
            _stopping = Task.Run(bot.Dispose);
            return _stopping;
        }
    }

    /// <summary>
    /// Шёпот от этого персонажа (<see cref="GameCaller.PrivateChat"/>) — свои вызовы на время отправки, как у Probe: работает и
    /// при остановленном боте, и рядом с работающим (вызовы идут через один обработчик окна игры по очереди). Ждёт вызов — до
    /// 5 с: звать не из потока окна. Отправку не проверить. Строку «Вы говорите с Ник: текст» игра у отправителя сама не пишет
    /// (это делает окно чата) — бот дописывает её в чат так же, как окно чата (<see cref="GameCaller.AddChatMessage"/>); не
    /// вышло — шёпот всё равно ушёл, в лог — предупреждение.
    /// </summary>
    public CallResult Whisper(string nick, string text)
    {
        if (_disposed)
            return CallResult.Refused("отключено");
        try
        {
            using var calls = GameCalls.Open(_game, _roots, _profile, CallTransport.Window, out var problem);
            if (calls is null)
                return CallResult.Refused(problem);

            var sent = calls.Caller.PrivateChat(nick, text);
            if (sent.Ok)
            {
                // Шёпот уже ушёл: что бы ни случилось со строкой о нём — это не отказ
                try
                {
                    ShowWhisperSent(calls.Caller, nick, text);
                }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    _log.Warning($"Шёпот ушёл, но строку о нём в чат не дописать: {e.Message}");
                }
            }

            return sent;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return CallResult.Refused(e.Message);
        }
    }

    // «Вы говорите с Ник: текст» в чат отправителя — по шаблону из файла клиента
    private void ShowWhisperSent(GameCaller caller, string nick, string text)
    {
        if (_profile.Ui.WhisperSentText == 0 || !_profile.Functions.ContainsKey(GameFunctions.AddChatMessage))
            return;

        string? problem = null;
        _uiStrings ??= UiStrings.LoadFromGameDirectory(Path.GetDirectoryName(_game.MainModulePath)!, _profile.GameFiles.Pck,
            out problem);
        var template = _uiStrings?.Get(_profile.Ui.WhisperSentText);
        var line = template is null ? null : GameCaller.FormatChatLine(template, nick, text);
        var shown = line is not null ? caller.AddChatMessage(line, GameCaller.ChatChannelWhisper)
            : CallResult.Refused(problem ?? $"в {UiStrings.InGameFile} нет строки {_profile.Ui.WhisperSentText} «… %s … %s»");
        if (!shown.Ok)
            _log.Warning($"Шёпот ушёл, но строку о нём в чат не дописать: {shown.Details}");
    }

    /// <summary>Сторож связи — только пока бот работает и профиль знает окна клиента (иначе разрыв не увидеть).</summary>
    private void WatchLink()
    {
        if (!LoginScreen.Supported(_profile))
            return;

        var watch = new LinkWatch(() => !_game.HasExited, ReadScreen, _profile.LoginScreen.Disconnected);
        watch.Lost += reason => LinkLost?.Invoke(reason);
        lock (_lock)
        {
            // «Стоп» успел раньше — сторож не нужен
            if (_bot is null)
                return;
            _watch?.Dispose();
            _watch = watch;
        }

        watch.Start();
    }

    /// <summary>Экран клиента: мир, вход, окно-сообщение. Не прочитался — <see cref="LoginView.None"/>.</summary>
    public LoginView ReadScreen()
    {
        try
        {
            _screen ??= new LoginScreen(_game, _roots, _profile);
            return _screen.Read();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return LoginView.None;
        }
    }

    // Прокачка: места мобов, NPC и ресурсов — вшитый справочник и поверх него файл клиента
    private QuestGuide LoadQuestGuide()
    {
        var directory = Path.GetDirectoryName(_game.MainModulePath)!;
        var points = GuidePoints.LoadFromGameDirectory(directory, _profile.GameFiles, out var problem, out var fromClient);
        if (problem is not null)
            _log.Warning($"Места мобов и NPC: {problem}, взяты из справочника бота ({points.Count})");
        else
            _log.Debug($"Места мобов и NPC: {points.Count}, из клиента {fromClient}");
        return new QuestGuide(points, _profile.QuestScripts);
    }

    private void LoadSkillsInBackground()
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

            if (_profile.Macros.List != 0)
            {
                _macroIcons = IconAtlas.LoadFromGameDirectory(directory, "skillgrp", pck, out problem);
                if (problem is not null)
                    _log.Warning("Иконки макросов: " + problem);
            }

            try
            {
                var icons = SkillIcons.Load(_game, _profile, out problem);
                _skillIcons = icons;
                if (problem is not null)
                    _log.Warning("Иконки скиллов: " + problem);
                else
                    _log.Debug($"Иконки скиллов: {icons.Count}");
            }
            // Клиент закрылся посреди чтения — не беда, иконки не нужны
            catch (Exception e) when (e is MemoryAccessException or InvalidOperationException or Win32Exception)
            {
                _log.Debug("Иконки скиллов: " + e.Message);
            }
        });
    }

    /// <summary>
    /// Обработчик окна игры — один на все боты этого клиента (<see cref="Calls.WindowCallRunner"/>): «Стоп» его не снимает,
    /// снимаем при отключении. Только если метка клиента у нас: без метки на этом клиенте работает и бот другого окна —
    /// снимем обработчик, и тот встанет. Тогда оставляем: вреда нет, следующий «Старт» возьмёт его же.
    /// </summary>
    private void RemoveWindowHandler()
    {
        if (_clientLock is null)
        {
            _log.Debug("Обработчик окна игры оставляю: клиент подключён и в другом окне");
            return;
        }

        if (_game.HasExited)
            return;

        _log.Debug(GameCalls.RemoveWindowHandler(_game, out var details)
            ? "Обработчик окна игры снят"
            : "Обработчик окна игры не снят: " + details);
    }

    /// <summary>
    /// Отключиться. Сначала бот — ждём его остановку до <see cref="StopWait"/> (окно может замереть, если как раз идёт вызов
    /// в игру). Не дождались — закрываемся дальше: бот закроет свой дескриптор вызовов сам, когда его ход выйдет; закрытие
    /// дескриптора чтения посреди чтения безопасно (SafeHandle держит счётчик ссылок, следующее чтение просто не удастся).
    /// Остановился — снимаем обработчик окна игры (<see cref="RemoveWindowHandler"/>), не дождались — оставляем: бот ещё
    /// может быть посреди вызова через него.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (StopBotAsync().Wait(StopWait))
            RemoveWindowHandler();
        else
            _log.Warning($"Бот не остановился за {StopWait.TotalSeconds:0} с — отключаюсь, он закроет вызовы сам, когда закончит ход");
        _monitor.Dispose();
        _game.Dispose();
        _clientLock?.Dispose();
    }
}

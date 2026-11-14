using System;
using System.Threading;
using Notes.Core.Brain;
using Notes.Core.Logging;
using Notes.Core.Memory;
using Notes.Core.Settings;
using Notes.Core.World;

namespace Notes.Core.Session;

/// <summary>
/// Работающий бот: на каждом снимке — ход мозга. Владеет мозгом и транспортом вызовов и закрывает их в правильном порядке.
/// <para>
/// Гарантия остановки: все вызовы в игру идут только из хода мозга, а он идёт под <c>_tick</c> и только пока бот не
/// останавливается. <see cref="Dispose"/> дожидается хода, который уже идёт (вызов в игру — до 5 с), и только потом закрывает
/// вызовы — после него в игру не уходит ни одного вызова. Отписки от ленты мало: событие копирует подписчиков до отписки,
/// и ход, начатый раньше, иначе прошёл бы параллельно с закрытием.
/// </para>
/// </summary>
public sealed class RunningBot : IDisposable
{
    private readonly object _tick = new();
    private readonly IWorldFeed _feed;
    private readonly IBotRunner _brain;
    private readonly IDisposable _calls;
    private readonly Func<DateTime, GameMemoryState> _memory;
    // Чем был занят перс на прошлом ходу, пока ждём свободного момента для перезапуска
    private string? _waitingFor;
    private readonly Func<string?> _broken;
    private readonly ILogger _log;
    private volatile bool _stopping;
    // Поток, который сейчас делает ход: Dispose из самого хода (подписчик StopRequested) не ждёт себя, а просит закрыть после хода
    private volatile Thread? _ticking;
    private bool _closeAfterTick;
    private int _disposed;
    private int _closed;
    // Бот сам попросил остановку или перезапуск (кончается память игры, потеряна связь с окном) — ходов больше нет
    private bool _halted;

    /// <param name="calls">Транспорт вызовов (<see cref="GameCalls"/>); закрывается последним.</param>
    /// <param name="memory">Память игры по времени снимка (<see cref="GameMemoryGuard"/>): мало или критично — перезапуск клиента.</param>
    /// <param name="broken">После хода: вызовы больше не доходят (<see cref="GameCalls.Broken"/>) — причина; null — всё в порядке.</param>
    public RunningBot(IWorldFeed feed, IBotRunner brain, IDisposable calls, Func<DateTime, GameMemoryState>? memory = null,
        Func<string?>? broken = null, ILogger? log = null)
    {
        _feed = feed;
        _brain = brain;
        _calls = calls;
        _memory = memory ?? (_ => GameMemoryState.Ok);
        _broken = broken ?? (() => null);
        _log = log ?? NullLogger.Instance;
        brain.StatusChanged += status => StatusChanged?.Invoke(status);
        brain.StopRequested += reason => StopRequested?.Invoke(reason);
    }

    /// <summary>Мозг — для окна (номер точки маршрута); управлять им снаружи нельзя.</summary>
    public IBotRunner Brain => _brain;

    /// <summary>Начать ходить по снимкам. Отдельно от конструктора: сначала подписаться на события бота, потом пойдут ходы.</summary>
    public void Start()
    {
        if (!_stopping)
            _feed.Updated += OnWorld;
    }

    /// <summary>Что делает бот — из потока снимков.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Бот сам просит остановку (персонаж погиб, связь с окном игры потеряна) — из потока снимков, посреди хода.</summary>
    public event Action<string>? StopRequested;

    /// <summary>
    /// У игры кончается память — бот просит закрыть клиент и войти заново (<see cref="GameMemoryGuard"/>): «мало» — после хода,
    /// когда перс свободен, «критично» — сразу. Из потока снимков, посреди хода, один раз; ходов после него нет.
    /// </summary>
    public event Action<string>? RestartRequested;

    public void UpdateSettings(BotSettings settings) => _brain.UpdateSettings(settings);

    private void OnWorld(WorldState world)
    {
        if (_stopping)
            return;

        bool closeNow;
        lock (_tick)
        {
            if (_stopping)
                return;

            _ticking = Thread.CurrentThread;
            try
            {
                var memory = _halted ? GameMemoryState.Ok : _memory(world.Time);
                if (_halted)
                {
                }
                else if (memory == GameMemoryState.Critical)
                {
                    Restart("у игры кончается память");
                }
                else
                {
                    _brain.Tick(world);
                    if (_broken() is { } why)
                    {
                        _log.Warning($"{char.ToUpper(why[0])}{why.Substring(1)} — бот остановлен, нажмите «Старт»");
                        Halt(why);
                    }
                    else if (memory == GameMemoryState.Low)
                    {
                        // Ждём хода, после которого перс ничем не занят; о том, чего ждём, — в лог при каждой смене
                        var busy = _brain.Busy;
                        if (busy is null)
                        {
                            _log.Info("Перс свободен — перезапускаю клиент: у игры мало памяти");
                            Restart("у игры мало памяти");
                        }
                        else if (busy != _waitingFor)
                        {
                            _log.Info($"Перезапуск клиента ждёт: {busy}");
                        }

                        _waitingFor = busy;
                    }
                }
            }
            finally
            {
                _ticking = null;
                closeNow = _closeAfterTick;
            }
        }

        if (closeNow)
            Close();
    }

    // Просим остановку один раз; ходов больше нет — остановит тот, кто подписан
    private void Halt(string reason)
    {
        _halted = true;
        StopRequested?.Invoke(reason);
    }

    // Просим перезапуск клиента один раз; ходов больше нет — закроет клиент и войдёт заново тот, кто подписан
    private void Restart(string reason)
    {
        _halted = true;
        RestartRequested?.Invoke(reason);
    }

    /// <summary>
    /// Остановить: больше ни одного хода; дождаться хода, который идёт; сбросить мозг; вернуть обработчик окна игры и закрыть
    /// дескриптор вызовов. Из самого хода (подписчик <see cref="StopRequested"/>) — не ждёт себя: закроет сразу после хода.
    /// Повторный вызов ничего не делает.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stopping = true;
        _feed.Updated -= OnWorld;
        if (_ticking == Thread.CurrentThread)
        {
            _closeAfterTick = true;
            return;
        }

        Close();
    }

    private void Close()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        lock (_tick)
            _brain.Reset();
        _calls.Dispose();
    }
}

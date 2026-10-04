using System;
using System.Threading;
using BotCH.Core.Brain;
using BotCH.Core.Settings;
using BotCH.Core.World;

namespace BotCH.Core.Session;

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
    private readonly Func<DateTime, bool> _outOfMemory;
    private volatile bool _stopping;
    // Поток, который сейчас делает ход: Dispose из самого хода (подписчик StopRequested) не ждёт себя, а просит закрыть после хода
    private volatile Thread? _ticking;
    private bool _closeAfterTick;
    private int _disposed;
    private int _closed;
    private bool _memoryStop;

    /// <param name="calls">Транспорт вызовов (<see cref="GameCalls"/>); закрывается последним.</param>
    /// <param name="outOfMemory">Проверка памяти игры по времени снимка: true — пора остановиться (<see cref="Memory.GameMemoryGuard"/>).</param>
    public RunningBot(IWorldFeed feed, IBotRunner brain, IDisposable calls, Func<DateTime, bool>? outOfMemory = null)
    {
        _feed = feed;
        _brain = brain;
        _calls = calls;
        _outOfMemory = outOfMemory ?? (_ => false);
        brain.StatusChanged += status => StatusChanged?.Invoke(status);
        brain.StopRequested += reason => StopRequested?.Invoke(reason);
        feed.Updated += OnWorld;
    }

    /// <summary>Что делает бот — из потока снимков.</summary>
    public event Action<string>? StatusChanged;

    /// <summary>Бот сам просит остановку (персонаж погиб, у игры кончается память) — из потока снимков, посреди хода.</summary>
    public event Action<string>? StopRequested;

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
                if (_memoryStop || _outOfMemory(world.Time))
                {
                    // Просим один раз; ходов больше нет — дальше остановит тот, кто подписан
                    if (!_memoryStop)
                    {
                        _memoryStop = true;
                        StopRequested?.Invoke("у игры кончается память");
                    }
                }
                else
                {
                    _brain.Tick(world);
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

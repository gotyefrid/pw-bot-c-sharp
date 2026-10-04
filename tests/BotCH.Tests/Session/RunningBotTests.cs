using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BotCH.Core.Brain;
using BotCH.Core.Session;
using BotCH.Core.Settings;
using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Session;

/// <summary>Остановка бота: ни одного вызова в игру после неё, даже если ход уже шёл или «Стоп» пришёл из самого хода.</summary>
public class RunningBotTests
{
    private static readonly TimeSpan Long = TimeSpan.FromSeconds(5);

    private readonly List<string> _events = [];
    private readonly Feed _feed = new();
    private readonly Calls _calls;
    private readonly Brain _brain;
    private readonly WorldState _world = new FakeWorld().Snapshot();

    public RunningBotTests()
    {
        _calls = new Calls(_events);
        _brain = new Brain(_events, _calls);
    }

    private sealed class Feed : IWorldFeed
    {
        public event Action<WorldState>? Updated;
        public event Action<string>? Failed
        {
            add { }
            remove { }
        }

        public void Push(WorldState world) => Updated?.Invoke(world);

        // Подписчики, скопированные до отписки, — как у события, которое уже начало рассылку
        public Action<WorldState>? Captured() => Updated;
    }

    private sealed class Calls(List<string> events) : IDisposable
    {
        public volatile bool Disposed;

        public void Call()
        {
            lock (events)
                events.Add(Disposed ? "вызов после закрытия!" : "вызов");
        }

        public void Dispose()
        {
            lock (events)
                events.Add("закрыты");
            Disposed = true;
        }
    }

    private sealed class Brain(List<string> events, Calls calls) : IBotRunner
    {
        public Action? DuringTick { get; set; }

        public string Status => "";
        public event Action<string>? StatusChanged;
        public event Action<string>? StopRequested;

        public void RequestStop(string reason) => StopRequested?.Invoke(reason);

        public void Tick(WorldState world)
        {
            StatusChanged?.Invoke("ход");
            DuringTick?.Invoke();
            calls.Call();
        }

        public void UpdateSettings(BotSettings settings)
        {
        }

        public void Reset()
        {
            lock (events)
                events.Add("сброс");
        }

        public T? Part<T>() where T : class => null;
    }

    private RunningBot Start(Func<DateTime, bool>? outOfMemory = null, Func<string?>? broken = null)
    {
        var bot = new RunningBot(_feed, _brain, _calls, outOfMemory, broken);
        bot.Start();
        return bot;
    }

    private string[] Events()
    {
        lock (_events)
            return _events.ToArray();
    }

    [Fact]
    public void EachSnapshotIsOneTick()
    {
        using var bot = Start();

        _feed.Push(_world);
        _feed.Push(_world);

        Assert.Equal(["вызов", "вызов"], Events());
    }

    [Fact]
    public void DisposeResetsBrainThenClosesCalls()
    {
        var bot = Start();
        _feed.Push(_world);

        bot.Dispose();
        _feed.Push(_world);

        Assert.Equal(["вызов", "сброс", "закрыты"], Events());
    }

    [Fact]
    public void TickStartedBeforeUnsubscribeDoesNotRunAfterDispose()
    {
        // Событие скопировало подписчиков до отписки: такой «опоздавший» ход не должен дойти до мозга
        var bot = Start();
        var late = _feed.Captured()!;

        bot.Dispose();
        late(_world);

        Assert.Equal(["сброс", "закрыты"], Events());
    }

    [Fact]
    public void DisposeWaitsForCallInProgress()
    {
        // «Стоп» посреди вызова в игру: вызовы закрываются только после того, как ход закончился
        var bot = Start();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        _brain.DuringTick = () =>
        {
            entered.Set();
            release.Wait(Long);
        };
        var tick = Task.Run(() => _feed.Push(_world));
        Assert.True(entered.Wait(Long));

        var dispose = Task.Run(bot.Dispose);
        Assert.False(dispose.Wait(TimeSpan.FromMilliseconds(200)));
        Assert.False(_calls.Disposed);

        release.Set();
        Assert.True(dispose.Wait(Long));
        Assert.True(tick.Wait(Long));
        Assert.Equal(["вызов", "сброс", "закрыты"], Events());
    }

    [Fact]
    public void DisposeFromStopRequestedInsideTickClosesAfterTheTick()
    {
        // Подписчик StopRequested останавливает бота синхронно, посреди хода: не виснет и не закрывает вызовы, пока ход идёт
        var bot = Start();
        bot.StopRequested += _ => bot.Dispose();
        _brain.DuringTick = () => _brain.RequestStop("тест");

        var tick = Task.Run(() => _feed.Push(_world));

        Assert.True(tick.Wait(Long));
        _feed.Push(_world);
        Assert.Equal(["вызов", "сброс", "закрыты"], Events());
    }

    [Fact]
    public void SecondDisposeDoesNothing()
    {
        var bot = Start();

        bot.Dispose();
        bot.Dispose();

        Assert.Equal(["сброс", "закрыты"], Events());
    }

    [Fact]
    public void LostWindowAfterMoveAsksToStopOnceAndMakesNoMoreMoves()
    {
        // Связь с окном игры потеряна — бот не делает вид, что работает: просит остановку с причиной
        using var bot = Start(broken: () => "связь с окном игры потеряна");
        var reasons = new List<string>();
        bot.StopRequested += reasons.Add;

        _feed.Push(_world);
        _feed.Push(_world);

        Assert.Equal(["связь с окном игры потеряна"], reasons);
        Assert.Equal(["вызов"], Events());
    }

    [Fact]
    public void GameOutOfMemoryAsksToStopOnceAndMakesNoMoreMoves()
    {
        using var bot = Start(outOfMemory: _ => true);
        var reasons = new List<string>();
        bot.StopRequested += reasons.Add;

        _feed.Push(_world);
        _feed.Push(_world);

        Assert.Equal(["у игры кончается память"], reasons);
        Assert.Empty(Events());
    }
}

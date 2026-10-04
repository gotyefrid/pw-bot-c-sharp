using System;
using System.Diagnostics;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Session;
using BotCH.Core.Settings;
using Xunit;

namespace BotCH.Tests.Session;

/// <summary>
/// Подключение к процессу самих тестов: окна игры у него нет, функций игры — тоже, снимки не запускаем. Поэтому в игру ничего
/// не вызывается, а запуск и остановка бота проходят по-настоящему.
/// </summary>
public class ClientConnectionTests : IDisposable
{
    private readonly ClientConnection _connection = ClientConnection.Open(
        Process.GetCurrentProcess().Id, new ProfileCatalog().Load("comeback146").Data, new Logger());

    public void Dispose() => _connection.Dispose();

    private StartResult Start(CallTransport transport) => _connection.StartBot(BotMode.FarmMobs, new BotSettings(), 0, transport);

    [Fact]
    public void WindowNotFoundOffersFallbackInsteadOfSwitchingSilently()
    {
        var result = Start(CallTransport.Window);

        Assert.Equal(StartStatus.WindowFailed, result.Status);
        Assert.Equal("окно игры не найдено", result.Problem);
        Assert.Null(_connection.Bot);
    }

    [Fact]
    public void FallbackStartsBot()
    {
        var result = Start(CallTransport.Thread);

        Assert.Equal(StartStatus.Started, result.Status);
        Assert.NotNull(_connection.Bot);
    }

    [Fact]
    public void SecondStopWhileStoppingIsTheSameStop()
    {
        Start(CallTransport.Thread);

        var first = _connection.StopBotAsync();
        var second = _connection.StopBotAsync();

        Assert.Same(first, second);
        Assert.True(first.Wait(ClientConnection.StopWait));
        Assert.Null(_connection.Bot);
        Assert.False(_connection.IsStopping);
    }

    [Fact]
    public void StartWhileRunningIsRefused()
    {
        Start(CallTransport.Thread);

        var again = Start(CallTransport.Thread);

        Assert.Equal(StartStatus.Failed, again.Status);
    }

    [Fact]
    public void AfterStopBotCanStartAgain()
    {
        Start(CallTransport.Thread);
        Assert.True(_connection.StopBotAsync().Wait(ClientConnection.StopWait));

        Assert.Equal(StartStatus.Started, Start(CallTransport.Thread).Status);
    }

    [Fact]
    public void StopThenDisconnectStopsOnceAndDisconnectTwiceIsHarmless()
    {
        Start(CallTransport.Thread);

        _connection.StopBotAsync();
        _connection.Dispose();
        _connection.Dispose();

        Assert.Null(_connection.Bot);
        Assert.False(_connection.IsStopping);
    }

    [Fact]
    public void WithoutClientMarkWindowHandlerIsLeft()
    {
        // Метка клиента у первого подключения: второе на тот же клиент обработчик окна не снимает — он общий с ботом первого
        var log = new RingBufferSink();
        var second = ClientConnection.Open(Process.GetCurrentProcess().Id, new ProfileCatalog().Load("comeback146").Data,
            new Logger { MinLevel = LogLevel.Debug }.AddSink(log));

        second.Dispose();

        Assert.Contains(log.Snapshot(), e => e.Message.StartsWith("Обработчик окна игры оставляю", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Snapshot(), e => e.Message.StartsWith("Обработчик окна игры снят", StringComparison.Ordinal));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Notes.Core.Clients;
using Notes.Core.Profiles;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clients;

/// <summary>Вход по этапам на поддельном клиенте, который ведёт себя как живой PW Classic.</summary>
public class LoginFlowTests
{
    private static readonly LoginScreenOffsets Texts = ProfileCatalog.Default().Load("pwclassic136").Data.LoginScreen;

    private readonly FakeLoginClient _client = new();

    private LoginFlow Flow() => new(_client, _client, _client.HostName, () => _client.Alive, Texts, _client);

    private LoginResult Enter(string nick = "Флай", string password = "secret", CancellationToken token = default)
        => Flow().Enter("login", password, nick, null, token);

    [Fact]
    public void LogsInPicksTheNickAndWaitsForWorld()
    {
        _client.LoginScreenAt = _client.Now.AddSeconds(5);

        var result = Enter();

        Assert.Equal(LoginOutcome.InWorld, result.Outcome);
        Assert.Equal("Флай", result.Nick);
        Assert.Equal("Флай", _client.Host);
        Assert.Equal("secret", _client.TypedPassword);
        // Флай — второй: одна стрелка вниз
        Assert.Contains("↓", _client.Input.SkipWhile(i => i != "Enter").Skip(1).First());
    }

    [Fact]
    public void OldLoginInTheFieldIsErased()
    {
        _client.Account = "probetest";

        Assert.True(Enter().Ok);
        Assert.Equal("login", _client.Account);
    }

    [Fact]
    public void WrongPasswordIsFinal()
    {
        var result = Enter(password: "wrong");

        Assert.Equal(LoginOutcome.WrongPassword, result.Outcome);
        Assert.True(result.Final);
        Assert.Equal(FakeLoginClient.WrongPasswordText, result.Text);
    }

    [Fact]
    public void NoServerThenTheSameClientLogsInWhenServerIsBack()
    {
        _client.ServerDown = true;
        var first = Enter();
        Assert.Equal(LoginOutcome.NoServer, first.Outcome);
        Assert.False(first.Final);
        Assert.False(first.NeedsNewClient);

        // Сообщение так и висит; следующая попытка его закрывает и входит
        _client.ServerDown = false;
        Assert.True(Enter().Ok);
        Assert.Null(_client.Message);
    }

    [Fact]
    public void SilentServerIsStuck()
    {
        _client.ServerSilent = true;

        var result = Enter();

        Assert.Equal(LoginOutcome.Stuck, result.Outcome);
        Assert.True(result.NeedsNewClient);
        Assert.Contains("сервер не ответил", result.Text);
    }

    [Fact]
    public void LoginScreenNeverAppearsIsStuck()
    {
        _client.LoginScreenAt = _client.Now.AddHours(1);

        var result = Enter();

        Assert.Equal(LoginOutcome.Stuck, result.Outcome);
        Assert.Contains("экран логина не появился", result.Text);
    }

    [Fact]
    public void UnknownNickIsFinal()
    {
        var result = Enter(nick: "Чужой");

        Assert.Equal(LoginOutcome.NoCharacter, result.Outcome);
        Assert.Contains("Купец, Флай", result.Text);
    }

    [Fact]
    public void ClosedClientAndCancel()
    {
        _client.Alive = false;
        Assert.Equal(LoginOutcome.Exited, Enter().Outcome);

        _client.Alive = true;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        Assert.Equal(LoginOutcome.Cancelled, Enter(token: cancel.Token).Outcome);
    }

    [Fact]
    public void RunnerRetriesInTheSameClientAfterNoServer()
    {
        _client.ServerDown = true;
        var launches = 0;
        var runner = Runner(() => ++launches, _ => throw new InvalidOperationException("закрывать не надо"));
        var waits = new List<string>();

        // Сервер вернулся к концу первой паузы
        var run = runner.Run("login", "secret", "Флай", text =>
        {
            waits.Add(text);
            if (text.Contains("ещё раз через"))
                _client.ServerDown = false;
        });

        Assert.True(run.Result.Ok);
        Assert.Equal(1, launches);
        Assert.Equal(2, run.Attempts);
        Assert.Contains(waits, w => w.StartsWith(FakeLoginClient.NoServerText) && w.EndsWith("ещё раз через 1 мин"));
    }

    [Fact]
    public void RunnerRelaunchesStuckClient()
    {
        _client.ServerSilent = true;
        var launches = new List<int>();
        var closed = new List<int>();
        var runner = Runner(() =>
        {
            launches.Add(100 + launches.Count);
            // Новый клиент — сервер уже отвечает
            _client.ServerSilent = launches.Count == 1;
            return launches.Last();
        }, closed.Add);

        var run = runner.Run("login", "secret", "Флай");

        Assert.True(run.Result.Ok);
        Assert.Equal([100, 101], launches);
        Assert.Equal([100], closed);
        Assert.Equal(101, run.Pid);
    }

    [Fact]
    public void RunnerStopsOnWrongPasswordAndAfterLastAttempt()
    {
        var runner = Runner(() => 1, _ => { });
        Assert.Equal(1, runner.Run("login", "wrong", "Флай").Attempts);

        _client.ServerDown = true;
        var run = runner.Run("login", "secret", "Флай");
        Assert.Equal(LoginOutcome.NoServer, run.Result.Outcome);
        Assert.Equal(4, run.Attempts);
        Assert.Contains("попыток: 4", run.Result.Text);
    }

    private LoginRunner Runner(Func<int> launch, Action<int> close) => new(launch, _ => new Attempt(Flow()), close, _client);

    private sealed class Attempt(LoginFlow flow) : ILoginAttempt
    {
        public LoginResult Enter(string login, string password, string nick, Action<string> progress, CancellationToken token)
            => flow.Enter(login, password, nick, progress, token);
    }
}

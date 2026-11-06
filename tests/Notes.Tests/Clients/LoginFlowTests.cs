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

    private LoginFlow Flow() => new(_client, _client, _client.HostName, () => _client.Alive, Texts, _client, _client);

    private static readonly string[] AllButtons =
        [
            GameFunctions.UiSetText, GameFunctions.LoginConfirm, GameFunctions.SelectRole, GameFunctions.SelectPlay, GameFunctions.AgreementAccept,
            GameFunctions.DialogCommand,
        ];

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
    public void ComebackAcceptsAgreementAndClicksIntoTheEmptyFocus()
    {
        _client.Agreement = true;
        _client.DropFocus();
        _client.Account = "probetest";

        var result = Enter();

        Assert.Equal(LoginOutcome.InWorld, result.Outcome);
        Assert.Equal("login", _client.Account);
        Assert.Equal("secret", _client.TypedPassword);
        Assert.Equal("Enter", _client.Input[0]);
        Assert.Equal($"щелчок {FakeLoginClient.AccountAt.X},{FakeLoginClient.AccountAt.Y}", _client.Input[1]);
    }

    [Fact]
    public void ComebackKeepsTheOldPasswordAfterAnErrorItIsErased()
    {
        _client.KeepsFields = true;
        _client.ServerDown = true;
        Assert.Equal(LoginOutcome.NoServer, Enter().Outcome);
        Assert.Equal("secret", _client.TypedPassword);

        _client.ServerDown = false;
        Assert.True(Enter().Ok);
        Assert.Equal("secret", _client.TypedPassword);
    }

    [Fact]
    public void HintOverSelectTakesTheFirstEnter()
    {
        _client.Hint = true;

        Assert.True(Enter().Ok);
        Assert.Equal(2, _client.Input.SkipWhile(i => !i.Contains("↓")).Count(i => i == "Enter"));
    }

    [Fact]
    public void ButtonsByGameFunctionsWithoutAnyKey()
    {
        // Всё неудобное для клавиш сразу: соглашение, нет фокуса, старый логин и пароль в полях, подсказка поверх выбора
        _client.Functions.UnionWith(AllButtons);
        _client.Agreement = true;
        _client.DropFocus();
        _client.KeepsFields = true;
        _client.Account = "probetest";
        _client.Hint = true;

        var result = Enter();

        Assert.Equal(LoginOutcome.InWorld, result.Outcome);
        Assert.Equal("login", _client.Account);
        Assert.Equal("secret", _client.TypedPassword);
        Assert.All(_client.Input, i => Assert.StartsWith("вызов: ", i));
        Assert.Equal(["вызов: соглашение", $"вызов: текст {LoginScreen.AccountField}", $"вызов: текст {LoginScreen.PasswordField}",
            "вызов: вход", "вызов: персонаж 2", "вызов: играть"], _client.Input);
    }

    [Fact]
    public void LeftoverMessageIsClosedByGameFunction()
    {
        // Прошлая попытка оставила «сервер недоступен»; следующая закрывает его вызовом, как кнопкой «Да», и входит без клавиш
        _client.Functions.UnionWith(AllButtons);
        _client.ServerDown = true;
        Assert.Equal(LoginOutcome.NoServer, Enter().Outcome);
        _client.Input.Clear();

        _client.ServerDown = false;
        Assert.True(Enter().Ok);
        Assert.Equal("вызов: сообщение", _client.Input[0]);
        Assert.All(_client.Input, i => Assert.StartsWith("вызов: ", i));
    }

    [Fact]
    public void FailedCallsFallBackToKeys()
    {
        _client.Functions.UnionWith(AllButtons);
        _client.CallsFail = true;

        Assert.True(Enter().Ok);
        Assert.Equal("secret", _client.TypedPassword);
        Assert.Contains("Enter", _client.Input);
    }

    [Fact]
    public void FocusInPlaceNoClick()
    {
        Assert.True(Enter().Ok);
        Assert.DoesNotContain(_client.Input, i => i.StartsWith("щелчок"));
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
            _client.Restart();
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

    [Fact]
    public void RunnerDoesNotCloseSomeoneElsesClient()
    {
        _client.ServerSilent = true;
        var runner = Runner(() => throw new InvalidOperationException("запускать не надо"),
            _ => throw new InvalidOperationException("чужой клиент не закрываем"));

        var run = runner.Run("login", "secret", "Флай", running: 77);

        Assert.Equal(LoginOutcome.Stuck, run.Result.Outcome);
        Assert.Equal(77, run.Pid);
    }

    private LoginRunner Runner(Func<int> launch, Action<int> close) => new(launch, _ => new Attempt(Flow()), close, _client);

    private sealed class Attempt(LoginFlow flow) : ILoginAttempt
    {
        public LoginResult Enter(string login, string password, string nick, Action<string> progress, CancellationToken token)
            => flow.Enter(login, password, nick, progress, token);
    }
}

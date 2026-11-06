using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Notes.Core.Memory;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.Core.Clients;

/// <summary>Одна попытка входа в запущенный клиент (<see cref="LoginFlow"/>).</summary>
public interface ILoginAttempt
{
    LoginResult Enter(string login, string password, string nick, Action<string> progress, CancellationToken token);
}

/// <param name="Pid">Клиент, в который вошли или который остался открытым; null — клиента нет (закрыт после зависания).</param>
public sealed record LoginRun(LoginResult Result, int? Pid, int Attempts);

/// <summary>
/// Вход с повторами. Клиент показал ошибку («нет связи», другое сообщение) — тот же клиент, ещё раз через паузу: он сам
/// подключится, когда сервер вернётся. Клиент завис или закрылся — закрыть свой клиент и запустить новый. Неверный пароль,
/// нет персонажа, отмена — сразу конец. Паузы — <see cref="Delays"/>, попыток на одну больше.
/// </summary>
public sealed class LoginRunner
{
    private readonly Func<int> _launch;
    private readonly Func<int, ILoginAttempt> _attach;
    private readonly Action<int> _close;
    private readonly ILoginClock _clock;

    /// <param name="launch">Запустить клиент — его PID.</param>
    /// <param name="attach">Попытка входа в клиент с этим PID.</param>
    /// <param name="close">Закрыть свой клиент (завис).</param>
    public LoginRunner(Func<int> launch, Func<int, ILoginAttempt> attach, Action<int> close, ILoginClock? clock = null)
    {
        _launch = launch;
        _attach = attach;
        _close = close;
        _clock = clock ?? SystemLoginClock.Instance;
    }

    public IReadOnlyList<TimeSpan> Delays { get; init; } = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(5)];

    public LoginRun Run(string login, string password, string nick, Action<string>? progress = null, CancellationToken token = default)
    {
        var say = progress ?? (_ => { });
        int? pid = null;
        for (var attempt = 1; ; attempt++)
        {
            if (pid is null)
            {
                say("запускаю клиент");
                pid = _launch();
            }

            var result = _attach(pid.Value).Enter(login, password, nick, say, token);
            if (result.Ok || result.Final)
                return new LoginRun(result, pid, attempt);

            if (result.NeedsNewClient)
            {
                _close(pid.Value);
                pid = null;
            }

            if (attempt > Delays.Count)
                return new LoginRun(result with { Text = $"{result.Text} (попыток: {attempt})" }, pid, attempt);

            var delay = Delays[attempt - 1];
            say($"{result.Text}; ещё раз через {Minutes(delay)}");
            _clock.Wait(delay, token);
            if (token.IsCancellationRequested)
                return new LoginRun(new LoginResult(LoginOutcome.Cancelled, "вход отменён"), pid, attempt);
        }
    }

    private static string Minutes(TimeSpan time)
        => time.TotalMinutes >= 1 ? $"{time.TotalMinutes:0} мин" : $"{time.TotalSeconds:0} с";
}

/// <summary>
/// Запущенный клиент для входа: экран входа и ник читаются из его памяти, клавиши — в его окно. Память открывается заново,
/// если не читается: клиент ещё грузится, база появляется не сразу.
/// </summary>
public sealed class ClientLoginAttempt : ILoginAttempt, ILoginScreen, IDisposable
{
    private readonly int _pid;
    private readonly ProfileData _profile;
    private readonly WindowKeys _keys;
    private GameProcess? _game;
    private GameRoots? _roots;

    public ClientLoginAttempt(int pid, ProfileData profile)
    {
        _pid = pid;
        _profile = profile;
        _keys = new WindowKeys(pid);
    }

    public LoginTimeouts Timeouts { get; init; } = new();

    public LoginResult Enter(string login, string password, string nick, Action<string> progress, CancellationToken token)
    {
        try
        {
            var flow = new LoginFlow(this, _keys, HostName, () => Alive(_pid), _profile.LoginScreen) { Timeouts = Timeouts };
            return flow.Enter(login, password, nick, progress, token);
        }
        finally
        {
            Dispose();
        }
    }

    public LoginView Read()
    {
        try
        {
            Open();
            var view = new LoginScreen(_game!, _roots!, _profile).Read();
            // Окна не читаются — клиент ещё грузится: база могла не найтись, и GameRoots это запомнил. В следующий раз — заново
            if (view.Stage == LoginStage.Unknown)
                Dispose();
            return view;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Dispose();
            return LoginView.None;
        }
    }

    private string? HostName()
    {
        try
        {
            Open();
            return new WorldReader(_game!, _roots!, _profile).TryReadHostName();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Dispose();
            return null;
        }
    }

    private void Open()
    {
        _game ??= GameProcess.Open(_pid);
        _roots ??= GameRoots.Find(_game, _game.MainModuleBase, _profile);
    }

    public void Dispose()
    {
        _game?.Dispose();
        _game = null;
        _roots = null;
    }

    /// <summary>Процесс жив.</summary>
    public static bool Alive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Закрыть клиент, который запустил сам бот (завис на входе), и дождаться, пока закроется.</summary>
    public static void Close(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(10_000);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Уже закрылся
        }
    }
}

using System;
using System.Linq;
using System.Threading;
using Notes.Core.Actions;
using Notes.Core.Calls;
using Notes.Core.Profiles;

namespace Notes.Core.Clients;

/// <summary>Клавиши и текст в окно клиента на экране входа (<see cref="WindowKeys"/>).</summary>
public interface ILoginKeys
{
    /// <summary>Нажать клавишу <paramref name="times"/> раз подряд (стереть поле, пройти по списку).</summary>
    CallResult Press(KeyStroke key, int times = 1);

    /// <summary>Напечатать текст в поле, где фокус.</summary>
    CallResult Type(string text);
}

/// <summary>Время для входа: настоящее или подставное в тестах (там ожидание двигает поддельный клиент).</summary>
public interface ILoginClock
{
    DateTime Now { get; }

    void Wait(TimeSpan time, CancellationToken token);
}

public sealed class SystemLoginClock : ILoginClock
{
    public static readonly SystemLoginClock Instance = new();

    public DateTime Now => DateTime.UtcNow;

    public void Wait(TimeSpan time, CancellationToken token) => token.WaitHandle.WaitOne(time);
}

/// <summary>Чем кончился вход.</summary>
public enum LoginOutcome
{
    /// <summary>Персонаж в мире.</summary>
    InWorld,

    /// <summary>Клиент сказал «неверный логин или пароль»: повторять бесполезно.</summary>
    WrongPassword,

    /// <summary>Нет связи с сервером («соединение разорвано»): войти ещё раз позже.</summary>
    NoServer,

    /// <summary>Клиент показал другое сообщение (текст — в <see cref="LoginResult.Text"/>): попробовать позже.</summary>
    Refused,

    /// <summary>На выборе персонажа нет нужного ника: повторять бесполезно.</summary>
    NoCharacter,

    /// <summary>Этап не сменился вовремя: клиент завис — закрыть и запустить заново.</summary>
    Stuck,

    /// <summary>Клиент закрылся.</summary>
    Exited,

    /// <summary>Вход отменили.</summary>
    Cancelled,
}

/// <param name="Text">Для игрока: что случилось (текст ошибки клиента как есть).</param>
public sealed record LoginResult(LoginOutcome Outcome, string Text, string? Nick = null)
{
    public bool Ok => Outcome == LoginOutcome.InWorld;

    /// <summary>Повтор не поможет: неверный пароль, нет персонажа или отменили.</summary>
    public bool Final => Outcome is LoginOutcome.WrongPassword or LoginOutcome.NoCharacter or LoginOutcome.Cancelled;

    /// <summary>Клиент закрылся или завис — повторять только с новым клиентом.</summary>
    public bool NeedsNewClient => Outcome is LoginOutcome.Stuck or LoginOutcome.Exited;
}

/// <summary>Сроки этапов входа.</summary>
public sealed record LoginTimeouts
{
    /// <summary>От запуска клиента до экрана логина (или выбора, или мира).</summary>
    public TimeSpan Screen { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>От «Вход» до выбора персонажа или сообщения: сервер отвечает за секунду, молчит — завис.</summary>
    public TimeSpan Answer { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>От «Войти» на выборе до персонажа в мире.</summary>
    public TimeSpan World { get; init; } = TimeSpan.FromSeconds(90);

    /// <summary>Поле или отметка откликнулись на нажатие.</summary>
    public TimeSpan Step { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Как часто смотреть на экран.</summary>
    public TimeSpan Poll { get; init; } = TimeSpan.FromMilliseconds(300);
}

/// <summary>
/// Вход в уже запущенный клиент по этапам, как человек: экран логина → логин, Tab, пароль, Enter → ответ сервера (выбор
/// персонажа или окно-сообщение) → свой ник стрелками → Enter → мир. Каждый шаг проверяется чтением экрана
/// (<see cref="ILoginScreen"/>): бот знает, где он, и отличает ошибку (клиент показал текст) от зависания (этап не сменился
/// вовремя). Повторы — снаружи (<see cref="LoginRunner"/>). Память только читается, в окно идут клавиши.
/// </summary>
public sealed class LoginFlow
{
    private static readonly KeyStroke EnterKey = new(0x0D), Tab = new(0x09), Backspace = new(0x08), Delete = new(0x2E),
        Up = new(0x26), Down = new(0x28);

    // Пароль не читается из памяти, его длина неизвестна: стираем с запасом (у PW пароль до 20 букв)
    private const int PasswordClear = 32;

    private readonly ILoginScreen _screen;
    private readonly ILoginKeys _keys;
    private readonly Func<string?> _hostName;
    private readonly Func<bool> _alive;
    private readonly LoginScreenOffsets _texts;
    private readonly ILoginClock _clock;

    /// <param name="hostName">Ник персонажа в мире (пусто — ещё не в мире).</param>
    /// <param name="alive">Процесс клиента жив.</param>
    public LoginFlow(ILoginScreen screen, ILoginKeys keys, Func<string?> hostName, Func<bool> alive, LoginScreenOffsets texts,
        ILoginClock? clock = null)
    {
        _screen = screen;
        _keys = keys;
        _hostName = hostName;
        _alive = alive;
        _texts = texts;
        _clock = clock ?? SystemLoginClock.Instance;
    }

    public LoginTimeouts Timeouts { get; init; } = new();

    /// <summary>Войти персонажем <paramref name="nick"/>. <paramref name="progress"/> — этапы простыми словами.</summary>
    public LoginResult Enter(string login, string password, string nick, Action<string>? progress = null,
        CancellationToken token = default)
    {
        try
        {
            return Run(login, password, nick, progress ?? (_ => { }), token);
        }
        catch (Stop stop)
        {
            return stop.Result;
        }
    }

    private LoginResult Run(string login, string password, string nick, Action<string> progress, CancellationToken token)
    {
        progress("жду экран логина");
        var view = WaitFor(v => v.Stage is LoginStage.Login or LoginStage.Select or LoginStage.World || v.Message is not null,
            Timeouts.Screen, "экран логина не появился", token);

        // Сообщение от прошлой попытки («соединение разорвано») — закрыть, как кнопкой «Да»
        if (view.Message is not null && view.Stage != LoginStage.World)
        {
            progress($"закрываю сообщение: {view.Message}");
            Press(EnterKey);
            view = WaitFor(v => v.Message is null, Timeouts.Step, "сообщение клиента не закрывается", token);
        }

        if (view.Stage == LoginStage.Login)
        {
            progress(string.IsNullOrEmpty(view.Ping) ? "экран логина" : $"экран логина, пинг {view.Ping}");
            TypeLogin(login, password, token);
            progress("логин и пароль введены, жду ответ сервера");
            Press(EnterKey);
            view = WaitFor(v => v.Stage is LoginStage.Select or LoginStage.World || v.Message is not null, Timeouts.Answer,
                "сервер не ответил на вход", token);
            if (view.Message is { } message)
                return Refusal(message);
        }

        if (view.Stage == LoginStage.Select)
        {
            progress("выбор персонажа");
            ChooseRole(nick, token);
            progress($"вхожу {nick}, жду мир");
            Press(EnterKey);
        }

        view = WaitFor(v => v.Stage == LoginStage.World && !string.IsNullOrEmpty(_hostName()) || v.Message is not null,
            Timeouts.World, "персонаж не появился в мире", token);
        if (view.Message is { } late && string.IsNullOrEmpty(_hostName()))
            return Refusal(late);

        var name = _hostName();
        return new LoginResult(LoginOutcome.InWorld, name == nick ? $"{name} в мире" : $"в мире {name}, а не {nick}", name);
    }

    // Логин — в поле логина с проверкой, что дошёл; Tab; пароль. Пароль из памяти не читается: проверит ответ сервера
    private void TypeLogin(string login, string password, CancellationToken token)
    {
        Focus(LoginScreen.AccountField, token);
        for (var attempt = 1; ; attempt++)
        {
            var current = _screen.Read().Account ?? "";
            Clear(current.Length + 2);
            Type(login);
            if (TryWaitFor(v => v.Account == login, Timeouts.Step, token) is not null)
                break;
            if (attempt == 2)
                throw Stuck($"логин не вводится в поле (там «{_screen.Read().Account}»)");
        }

        Press(Tab);
        Focus(LoginScreen.PasswordField, token);
        Clear(PasswordClear);
        Type(password);
    }

    // Фокус в нужное поле: Tab ходит между полями окна логина
    private void Focus(string field, CancellationToken token)
    {
        for (var tabs = 0; tabs <= 3; tabs++)
        {
            if (TryWaitFor(v => v.Focus == field, tabs == 0 ? TimeSpan.Zero : Timeouts.Step, token) is not null)
                return;
            Press(Tab);
        }

        throw Stuck($"не попасть в поле {field} (фокус: {_screen.Read().Focus ?? "нет"})");
    }

    // Стереть поле: курсор может стоять где угодно — и назад, и вперёд
    private void Clear(int count)
    {
        Press(Backspace, count);
        Press(Delete, count);
    }

    // Свой ник в списке — стрелками до него, с проверкой отметки
    private void ChooseRole(string nick, CancellationToken token)
    {
        var view = WaitFor(v => v.Stage != LoginStage.Select || v.Roles.Count > 0, Timeouts.Step, "список персонажей пуст", token);
        if (view.Stage != LoginStage.Select)
            throw Stuck("выбор персонажа закрылся сам");

        var role = view.Roles.FirstOrDefault(r => r.Name == nick)
                   ?? view.Roles.FirstOrDefault(r => string.Equals(r.Name, nick, StringComparison.OrdinalIgnoreCase));
        if (role is null)
        {
            throw new Stop(new LoginResult(LoginOutcome.NoCharacter,
                $"на аккаунте нет персонажа {nick} (есть: {string.Join(", ", view.Roles.Select(r => r.Name))})"));
        }

        for (var attempt = 1; ; attempt++)
        {
            var selected = _screen.Read().Selected ?? 1;
            var steps = role.Slot - selected;
            if (steps != 0)
                Press(steps > 0 ? Down : Up, Math.Abs(steps));
            if (TryWaitFor(v => v.Selected == role.Slot, Timeouts.Step, token) is not null)
                return;
            if (attempt == 2)
                throw Stuck($"не выбрать {nick}: отмечен {_screen.Read().Selected?.ToString() ?? "никто"}");
        }
    }

    private LoginResult Refusal(string message)
    {
        var outcome = _texts.WrongPassword.Any(t => message.StartsWith(t, StringComparison.OrdinalIgnoreCase)) ? LoginOutcome.WrongPassword
            : _texts.NoServer.Any(t => message.StartsWith(t, StringComparison.OrdinalIgnoreCase)) ? LoginOutcome.NoServer
            : LoginOutcome.Refused;
        return new LoginResult(outcome, message);
    }

    private LoginView WaitFor(Func<LoginView, bool> done, TimeSpan timeout, string stuck, CancellationToken token)
        => TryWaitFor(done, timeout, token) ?? throw Stuck(stuck);

    // Экран, на котором условие выполнилось; null — не дождались. Закрытый клиент и отмена — сразу конец входа
    private LoginView? TryWaitFor(Func<LoginView, bool> done, TimeSpan timeout, CancellationToken token)
    {
        var until = _clock.Now + timeout;
        while (true)
        {
            if (token.IsCancellationRequested)
                throw new Stop(new LoginResult(LoginOutcome.Cancelled, "вход отменён"));
            if (!_alive())
                throw new Stop(new LoginResult(LoginOutcome.Exited, "клиент закрылся"));

            var view = _screen.Read();
            if (done(view))
                return view;
            if (_clock.Now >= until)
                return null;
            _clock.Wait(Timeouts.Poll, token);
        }
    }

    private void Press(KeyStroke key, int times = 1)
    {
        if (_keys.Press(key, times) is { Ok: false } result)
            throw Stuck($"клавиша не нажалась: {result.Details}");
    }

    private void Type(string text)
    {
        if (_keys.Type(text) is { Ok: false } result)
            throw Stuck($"текст не вводится: {result.Details}");
    }

    private Stop Stuck(string text) => new(new LoginResult(LoginOutcome.Stuck, $"{text} (сейчас: {Where(_screen.Read().Stage)})"));

    /// <summary>Этап простыми словами.</summary>
    public static string Where(LoginStage stage) => stage switch
    {
        LoginStage.Loading => "загрузка",
        LoginStage.Login => "экран логина",
        LoginStage.Select => "выбор персонажа",
        LoginStage.World => "в мире",
        _ => "окна клиента не читаются",
    };

    private sealed class Stop(LoginResult result) : Exception(result.Text)
    {
        public LoginResult Result { get; } = result;
    }
}

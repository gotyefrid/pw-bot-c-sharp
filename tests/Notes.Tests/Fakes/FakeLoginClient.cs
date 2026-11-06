using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Notes.Core.Actions;
using Notes.Core.Calls;
using Notes.Core.Clients;
using Notes.Core.Profiles;

namespace Notes.Tests.Fakes;

/// <summary>
/// Клиент на экране входа, как вёл себя живой PW Classic: буквы идут в поле с фокусом, Tab переключает поле, Backspace стирает,
/// Enter на логине — ответ сервера (выбор персонажа или сообщение), стрелки двигают отметку, Enter на выборе — мир через пару
/// секунд, Enter при сообщении — закрыть его. Время подставное: ожидание двигает часы.
/// </summary>
public sealed class FakeLoginClient : ILoginScreen, ILoginKeys, ILoginClock, ILoginCalls
{
    public const string WrongPasswordText = "Неверный пароль или имя учетной записи";
    public const string NoServerText = "Соединение с сервером было разорвано. Пожалуйста, попробуйте зайти еще раз.";

    private string? _focus = LoginScreen.AccountField;

    /// <summary>Где поле логина на экране: щелчок туда ставит в него фокус.</summary>
    public static readonly (int X, int Y) AccountAt = (933, 419);

    public DateTime Now { get; private set; } = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    public string Login { get; set; } = "login";
    public string Password { get; set; } = "secret";
    public List<string> Roles { get; } = ["Купец", "Флай"];

    /// <summary>Экран логина появится в это время (до него — загрузка).</summary>
    public DateTime LoginScreenAt { get; set; }

    public bool ServerDown { get; set; }

    /// <summary>Сервер молчит: на «Вход» ничего не происходит.</summary>
    public bool ServerSilent { get; set; }

    public bool Alive { get; set; } = true;

    /// <summary>
    /// Как Comeback 1.3.6: в памяти видно, сколько букв в поле пароля, после ошибки оба поля остаются как были. Стирание
    /// память не обновляет (на экране пусто, в памяти те же буквы) — только ввод.
    /// </summary>
    public bool KeepsFields { get; set; }

    private int _passwordInMemory;

    /// <summary>Поверх выбора персонажа — окно «Подсказка» (Comeback 1.3.6): первый Enter закрывает его.</summary>
    public bool Hint { get; set; }

    /// <summary>Перед экраном логина — пользовательское соглашение (Comeback): Enter принимает.</summary>
    public bool Agreement { get; set; }

    /// <summary>Фокуса нет ни в одном поле (свежий клиент Comeback): Tab не работает, пока не щёлкнуть в поле логина.</summary>
    public void DropFocus() => _focus = null;

    public LoginStage Stage { get; private set; } = LoginStage.Login;
    public string Account { get; set; } = "";
    public string TypedPassword { get; private set; } = "";
    public string? Message { get; set; }
    public int Selected { get; set; } = 1;
    public string? Host { get; private set; }

    private DateTime? _worldAt;

    /// <summary>Какие кнопки есть функциями игры (<see cref="GameFunctions"/>); пусто — только клавиши.</summary>
    public HashSet<string> Functions { get; } = [];

    /// <summary>Вызовы в игру не доходят (окно игры не приняло) — бот должен перейти на клавиши.</summary>
    public bool CallsFail { get; set; }

    /// <summary>Что нажимали и печатали — для проверок.</summary>
    public List<string> Input { get; } = [];

    public LoginView Read()
    {
        if (Now < LoginScreenAt)
            return LoginView.None with { Stage = LoginStage.Loading };
        if (Agreement)
            return LoginView.None with { Stage = LoginStage.Agreement };
        if (_worldAt is { } at && Now >= at)
        {
            Stage = LoginStage.World;
            Host ??= Roles[Selected - 1];
        }

        return Stage switch
        {
            LoginStage.Login => LoginView.None with
            {
                Stage = Stage, Ping = "344 ms", Account = Account, Focus = _focus, Message = Message, AccountAt = AccountAt,
                PasswordLength = KeepsFields ? _passwordInMemory : 0,
            },
            LoginStage.Select => LoginView.None with
            {
                Stage = Stage, Message = Message, Selected = Selected,
                Roles = Roles.Select((name, i) => new LoginRole(i + 1, name, "Друид Ур. 46")).ToList(),
            },
            _ => LoginView.None with { Stage = Stage, Message = Message },
        };
    }

    /// <summary>Новый клиент вместо закрытого: экран логина, поля пустые.</summary>
    public void Restart()
    {
        Stage = LoginStage.Login;
        Account = "";
        TypedPassword = "";
        Message = null;
        _focus = LoginScreen.AccountField;
    }

    public string? HostName() => Stage == LoginStage.World ? Host : null;

    public CallResult Click(int x, int y)
    {
        Input.Add($"щелчок {x},{y}");
        if (Stage == LoginStage.Login && Message is null && !Agreement && (x, y) == AccountAt)
            _focus = LoginScreen.AccountField;
        return CallResult.Done;
    }

    public CallResult Press(KeyStroke key, int times = 1)
    {
        Input.Add(times == 1 ? $"{key}" : $"{key}×{times}");
        for (var i = 0; i < times; i++)
            Key(key.Key);
        return CallResult.Done;
    }

    public CallResult Type(string text)
    {
        Input.Add($"«{text}»");
        if (Stage == LoginStage.Login && Message is null && !Agreement)
        {
            if (_focus == LoginScreen.AccountField)
                Account += text;
            else if (_focus == LoginScreen.PasswordField)
            {
                TypedPassword += text;
                _passwordInMemory = Math.Max(_passwordInMemory, TypedPassword.Length);
            }
        }

        return CallResult.Done;
    }

    public void Wait(TimeSpan time, CancellationToken token) => Now += time;

    public bool Can(string function) => Functions.Contains(function);

    public CallResult SetText(string field, string text) => Call($"текст {field}", () =>
    {
        if (Stage != LoginStage.Login || Message is not null || Agreement)
            return;
        if (field == LoginScreen.AccountField)
            Account = text;
        else
        {
            TypedPassword = text;
            _passwordInMemory = text.Length;
        }
    });

    // Как кнопка «Вход»: то же, что Enter на экране логина
    CallResult ILoginCalls.Login() => Call("вход", () => Key(0x0D));

    public CallResult SelectRole(int slot) => Call($"персонаж {slot}", () =>
    {
        if (Stage == LoginStage.Select && Message is null)
            Selected = slot;
    });

    // «Играть» мимо подсказки: она закрывается вместе с выбором
    public CallResult Play() => Call("играть", () =>
    {
        Hint = false;
        Key(0x0D);
    });

    public CallResult Agree() => Call("соглашение", () => Agreement = false);

    public CallResult CloseMessage() => Call("сообщение", () => Message = null);

    private CallResult Call(string what, Action action)
    {
        Input.Add($"вызов: {what}");
        if (CallsFail)
            return CallResult.Refused("окно игры не приняло вызов");
        action();
        return CallResult.Done;
    }

    private void Key(int key)
    {
        if (Agreement)
        {
            if (key == 0x0D)
                Agreement = false;
            return;
        }

        if (Message is not null)
        {
            // Окно-сообщение ловит Enter («Да»), остальное — мимо
            if (key == 0x0D)
                Message = null;
            return;
        }

        switch (Stage, key)
        {
            // Без фокуса Tab некуда двигать
            case (LoginStage.Login, 0x09) when _focus is not null:
                _focus = _focus == LoginScreen.AccountField ? LoginScreen.PasswordField : LoginScreen.AccountField;
                break;
            case (LoginStage.Login, 0x08):
                if (_focus == LoginScreen.AccountField && Account.Length > 0)
                    Account = Account.Substring(0, Account.Length - 1);
                else if (_focus == LoginScreen.PasswordField && TypedPassword.Length > 0)
                    TypedPassword = TypedPassword.Substring(0, TypedPassword.Length - 1);
                break;
            case (LoginStage.Login, 0x0D):
                if (ServerSilent)
                    break;
                if (ServerDown)
                    Message = NoServerText;
                else if (Account != Login || TypedPassword != Password)
                    Message = WrongPasswordText;
                else
                    Stage = LoginStage.Select;
                // Как у живого PW Classic после ошибки: поле логина пустое, фокус в нём
                if (Message is not null && !KeepsFields)
                {
                    Account = "";
                    TypedPassword = "";
                    _focus = LoginScreen.AccountField;
                }

                break;
            case (LoginStage.Select, 0x28):
                Selected = Math.Min(Selected + 1, Roles.Count);
                break;
            case (LoginStage.Select, 0x26):
                Selected = Math.Max(Selected - 1, 1);
                break;
            case (LoginStage.Select, 0x0D) when Hint:
                Hint = false;
                break;
            case (LoginStage.Select, 0x0D):
                Stage = LoginStage.Loading;
                _worldAt = Now + TimeSpan.FromSeconds(4);
                break;
        }
    }
}

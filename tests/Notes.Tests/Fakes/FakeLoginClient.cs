using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Notes.Core.Actions;
using Notes.Core.Calls;
using Notes.Core.Clients;

namespace Notes.Tests.Fakes;

/// <summary>
/// Клиент на экране входа, как вёл себя живой PW Classic: буквы идут в поле с фокусом, Tab переключает поле, Backspace стирает,
/// Enter на логине — ответ сервера (выбор персонажа или сообщение), стрелки двигают отметку, Enter на выборе — мир через пару
/// секунд, Enter при сообщении — закрыть его. Время подставное: ожидание двигает часы.
/// </summary>
public sealed class FakeLoginClient : ILoginScreen, ILoginKeys, ILoginClock
{
    public const string WrongPasswordText = "Неверный пароль или имя учетной записи";
    public const string NoServerText = "Соединение с сервером было разорвано. Пожалуйста, попробуйте зайти еще раз.";

    private string _focus = LoginScreen.AccountField;

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

    public LoginStage Stage { get; private set; } = LoginStage.Login;
    public string Account { get; set; } = "";
    public string TypedPassword { get; private set; } = "";
    public string? Message { get; set; }
    public int Selected { get; set; } = 1;
    public string? Host { get; private set; }

    private DateTime? _worldAt;

    /// <summary>Что нажимали и печатали — для проверок.</summary>
    public List<string> Input { get; } = [];

    public LoginView Read()
    {
        if (Now < LoginScreenAt)
            return LoginView.None with { Stage = LoginStage.Loading };
        if (_worldAt is { } at && Now >= at)
        {
            Stage = LoginStage.World;
            Host ??= Roles[Selected - 1];
        }

        return Stage switch
        {
            LoginStage.Login => LoginView.None with { Stage = Stage, Ping = "344 ms", Account = Account, Focus = _focus, Message = Message },
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
        if (Stage == LoginStage.Login && Message is null)
        {
            if (_focus == LoginScreen.AccountField)
                Account += text;
            else
                TypedPassword += text;
        }

        return CallResult.Done;
    }

    public void Wait(TimeSpan time, CancellationToken token) => Now += time;

    private void Key(int key)
    {
        if (Message is not null)
        {
            // Окно-сообщение ловит Enter («Да»), остальное — мимо
            if (key == 0x0D)
                Message = null;
            return;
        }

        switch (Stage, key)
        {
            case (LoginStage.Login, 0x09):
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
                // Как у живого клиента после ошибки: поле логина пустое, фокус в нём
                if (Message is not null)
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
            case (LoginStage.Select, 0x0D):
                Stage = LoginStage.Loading;
                _worldAt = Now + TimeSpan.FromSeconds(4);
                break;
        }
    }
}

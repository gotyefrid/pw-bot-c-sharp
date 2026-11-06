using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Clients;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.World;

namespace Notes.App.Panels;

/// <summary>Строка списка персонажей: сервер, ник, класс и уровень, какими бот видел персонажа последний раз.</summary>
public sealed record CharacterRow(CharacterAccount Account, string ServerName)
{
    public string Nick => Account.Nick;
    public string ClassText => PlayerClasses.Name(Account.Class) ?? "—";
    public string LevelText => Account.Level > 0 ? Account.Level.ToString() : "—";

    /// <summary>Комментарий одной строкой (в таблице); целиком — в подсказке и в окне правки.</summary>
    public string CommentLine => Account.Comment.Replace("\r\n", " ").Replace('\n', ' ').Trim();
    public string SeenText => Account.SeenAt is { } seen ? $"Видели в игре {seen:dd.MM.yyyy HH:mm}" : "Ещё не видели в игре";

    /// <summary>Подсказка строки: комментарий целиком и когда видели.</summary>
    public string Hint => Account.Comment.Trim().Length > 0 ? $"{Account.Comment.Trim()}\n\n{SeenText}" : SeenText;
}

/// <summary>
/// Окно «Персонажи»: список (<see cref="CharacterBook"/>), добавить, изменить, удалить и открыть — запустить клиент и войти.
/// Создаётся на каждое открытие окна: список читается с диска заново (его могла поменять другая копия бота).
/// </summary>
public sealed class CharactersPanel : ObservableObject
{
    private readonly CharacterBook _book;
    private readonly IReadOnlyList<ServerProfile> _servers;
    private readonly Func<string, string?> _gamePath;
    private readonly IDialogs _dialogs;
    private readonly ILogger _log;
    private readonly Action<CharacterAccount> _open;

    /// <param name="gamePath">«Игра» сервера по его id — клиент по умолчанию.</param>
    /// <param name="open">Запустить персонажа (окно бота): клиент, вход, подключение.</param>
    public CharactersPanel(CharacterBook book, IReadOnlyList<ServerProfile> servers, Func<string, string?> gamePath,
        IDialogs dialogs, ILogger log, Action<CharacterAccount> open)
    {
        _book = book;
        _servers = servers;
        _gamePath = gamePath;
        _dialogs = dialogs;
        _log = log;
        _open = open;
        AddCommand = new RelayCommand(Add);
        EditCommand = new RelayCommand(Edit, () => Selected is not null);
        RemoveCommand = new RelayCommand(Remove, () => Selected is not null);
        OpenCommand = new RelayCommand(Open, () => Selected is not null);
        var list = _book.Load(out var problem);
        if (problem is not null)
            _log.Warning(problem);
        Show(list, null);
    }

    public ObservableCollection<CharacterRow> Rows { get; } = new();

    private CharacterRow? _selected;

    public CharacterRow? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsEmpty => Rows.Count == 0;

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand OpenCommand { get; }

    /// <summary>Показать окно правки; true — «Сохранить». Задаёт окно, которое показывает список.</summary>
    public Func<CharacterDraft, bool>? EditRequested { get; set; }

    /// <summary>Персонажа открывают — окну списка пора закрыться.</summary>
    public event Action? Opened;

    private void Add()
    {
        var server = _servers.FirstOrDefault();
        var draft = new CharacterDraft(new CharacterAccount { Server = server?.Id ?? "" }, _servers, _gamePath, isNew: true);
        Save(draft);
    }

    private void Edit()
    {
        if (Selected is { } row)
            Save(new CharacterDraft(row.Account.Clone(), _servers, _gamePath, isNew: false));
    }

    private void Save(CharacterDraft draft)
    {
        if (EditRequested?.Invoke(draft) != true)
            return;

        try
        {
            var account = draft.Result();
            Show(_book.Save(account), account.Id);
            _log.Info($"Персонажи: {(draft.IsNew ? "добавлен" : "изменён")} {account.Nick} ({ServerName(account.Server)})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogs.Tell($"Не удалось сохранить список персонажей: {e.Message}");
        }
    }

    private void Remove()
    {
        if (Selected is not { } row || !_dialogs.AskYesNo($"Удалить {row.Nick} ({row.ServerName}) из списка? Сам персонаж в игре останется."))
            return;

        try
        {
            Show(_book.Remove(row.Account.Id), null);
            _log.Info($"Персонажи: удалён {row.Nick} ({row.ServerName})");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _dialogs.Tell($"Не удалось сохранить список персонажей: {e.Message}");
        }
    }

    private void Open()
    {
        if (Selected is not { } row)
            return;

        Opened?.Invoke();
        _open(row.Account.Clone());
    }

    private void Show(IEnumerable<CharacterAccount> list, string? select)
    {
        Rows.Clear();
        foreach (var account in list)
            Rows.Add(new CharacterRow(account, ServerName(account.Server)));
        Selected = Rows.FirstOrDefault(r => r.Account.Id == select) ?? Rows.FirstOrDefault();
        OnPropertyChanged(nameof(IsEmpty));
    }

    private string ServerName(string id) => _servers.FirstOrDefault(s => s.Id == id)?.Name ?? id;
}

/// <summary>
/// Персонаж в окне правки. В список уходит только по «Сохранить» (<see cref="Result"/>). Пароль здесь — открытым текстом,
/// пока окно открыто; на диск — только зашифрованным.
/// </summary>
public sealed class CharacterDraft : ObservableObject
{
    private readonly CharacterAccount _account;
    private readonly Func<string, string?> _gamePath;

    public CharacterDraft(CharacterAccount account, IReadOnlyList<ServerProfile> servers, Func<string, string?> gamePath, bool isNew)
    {
        _account = account;
        _gamePath = gamePath;
        IsNew = isNew;
        Servers = servers;
        _server = servers.FirstOrDefault(s => s.Id == account.Server) ?? servers.FirstOrDefault();
        _nick = account.Nick;
        _login = account.Login;
        _exePath = account.ExePath;
        _comment = account.Comment;
        ClearExeCommand = new RelayCommand(() => ExePath = "", () => ExePath.Length > 0);
    }

    public bool IsNew { get; }

    public string Title => IsNew ? "Новый персонаж" : $"Персонаж {_account.Nick}";

    public IReadOnlyList<ServerProfile> Servers { get; }

    private ServerProfile? _server;

    public ServerProfile? Server
    {
        get => _server;
        set
        {
            if (SetProperty(ref _server, value))
                OnPropertyChanged(nameof(ExeText));
        }
    }

    private string _nick;

    public string Nick
    {
        get => _nick;
        set => SetProperty(ref _nick, value);
    }

    private string _login;

    public string Login
    {
        get => _login;
        set => SetProperty(ref _login, value);
    }

    /// <summary>Новый пароль; пусто у старого персонажа — оставить прежний.</summary>
    public string Password { get; set; } = "";

    public string PasswordHint => IsNew ? "" : "Пусто — оставить прежний";

    private string _exePath;

    /// <summary>Свой exe клиента; пусто — «Игра» сервера.</summary>
    public string ExePath
    {
        get => _exePath;
        set
        {
            if (SetProperty(ref _exePath, value ?? ""))
            {
                OnPropertyChanged(nameof(ExeText));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string ExeText => ExePath.Length > 0
        ? ExePath
        : Server is { } server && _gamePath(server.Id) is { } game ? $"как «Игра»: {game}" : "как «Игра» сервера (не выбрана)";

    public ICommand ClearExeCommand { get; }

    private string _comment;

    public string Comment
    {
        get => _comment;
        set => SetProperty(ref _comment, value ?? "");
    }

    private string _error = "";

    public string Error
    {
        get => _error;
        private set => SetProperty(ref _error, value);
    }

    /// <summary>Можно ли сохранять; нет — причина в <see cref="Error"/>.</summary>
    public bool Validate()
    {
        Error = Server is null ? "Выберите сервер."
            : Nick.Trim().Length == 0 ? "Введите ник персонажа."
            : Login.Trim().Length == 0 ? "Введите логин."
            : IsNew && Password.Length == 0 ? "Введите пароль."
            : ExePath.Length > 0 && !File.Exists(ExePath) ? "Файла клиента нет — выберите другой."
            : "";
        return Error.Length == 0;
    }

    public CharacterAccount Result()
    {
        var account = _account.Clone();
        // Сменили сервер или ник — это другой персонаж: прежние класс и уровень не его
        if (account.Server != Server!.Id || account.Nick != Nick.Trim())
        {
            account.Class = null;
            account.Level = 0;
            account.SeenAt = null;
        }

        account.Server = Server.Id;
        account.Nick = Nick.Trim();
        account.Login = Login.Trim();
        account.ExePath = ExePath;
        account.Comment = Comment.Trim();
        if (Password.Length > 0)
            account.Password = PasswordVault.Protect(Password);
        return account;
    }
}

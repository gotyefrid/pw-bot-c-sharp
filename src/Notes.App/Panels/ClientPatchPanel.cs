using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Clients;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;

namespace Notes.App.Panels;

/// <summary>
/// Панель «Патч клиента» в карточке подключения: состояние файла, кнопки «Пропатчить»/«Откат», политика
/// подключения к клиенту без правок. Всё про правки живёт здесь, а не в модели окна. Зависимости — делегатами,
/// как у соседних панелей: текущий профиль, список клиентов и подключённый PID — из модели окна; политика и
/// запомненные пути exe — в настройках подключения (settings.json), сохранение — тоже модели окна.
/// </summary>
public sealed class ClientPatchPanel : ObservableObject
{
    private readonly Func<ServerProfile> _profile;
    private readonly Func<IReadOnlyList<GameClient>> _clients;
    private readonly Func<int?> _connectedPid;
    private readonly Func<ConnectionSettings> _settings;
    private readonly Action _saved;
    private readonly IDialogs _dialogs;
    private readonly ILogger _log;

    /// <param name="settings">Настройки подключения: политика и пути exe по серверам.</param>
    /// <param name="saved">Настройки подключения изменены — сохранить.</param>
    /// <param name="dialogs">Вопрос «подключиться без правок?», отказ, выбор exe, когда путь взять неоткуда.</param>
    public ClientPatchPanel(
        Func<ServerProfile> profile,
        Func<IReadOnlyList<GameClient>> clients,
        Func<int?> connectedPid,
        Func<ConnectionSettings> settings,
        Action saved,
        IDialogs dialogs,
        ILogger log)
    {
        _profile = profile;
        _clients = clients;
        _connectedPid = connectedPid;
        _settings = settings;
        _saved = saved;
        _dialogs = dialogs;
        _log = log;
        PatchCommand = new RelayCommand(Patch, () => Recipe is not null && !Checking && State is ClientPatchState.Original or ClientPatchState.Missing);
        RestorePatchCommand = new RelayCommand(Restore, () => Recipe is not null && !Checking && State == ClientPatchState.Patched);
    }

    /// <summary>Рецепт текущего сервера; null — серверу правки не нужны, строка патча в окне скрыта.</summary>
    public ClientPatchRecipe? Recipe => _profile().Data.Patch;

    /// <summary>Показывать строку патча: только у серверов с рецептом.</summary>
    public bool ShowPatch => Recipe is not null;

    private ClientPatchState _state = ClientPatchState.Missing;

    /// <summary>Состояние exe, чей путь показан (пересчёт — в фоне, <see cref="Refresh"/>).</summary>
    public ClientPatchState State
    {
        get => _state;
        private set
        {
            if (!SetProperty(ref _state, value))
                return;

            OnPropertyChanged(nameof(PatchText));
            OnPropertyChanged(nameof(PatchHint));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private bool _checking = true;

    /// <summary>
    /// Идёт проверка файла (хэш в фоне): <see cref="State"/> ещё не про показанный exe — кнопки неактивны.
    /// До первой проверки — тоже «проверяю».
    /// </summary>
    public bool Checking
    {
        get => _checking;
        private set
        {
            if (!SetProperty(ref _checking, value))
                return;

            OnPropertyChanged(nameof(PatchText));
            OnPropertyChanged(nameof(PatchHint));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // Номер последней проверки: результат прежней (сменили сервер или клиент, пока считался хэш) — выбрасываем
    private int _checkVersion;

    /// <summary>Что написано в строке «Патч» — коротко: окно бота фиксированной ширины, рядом две кнопки.</summary>
    public string PatchText => Checking ? "проверяю…" : State switch
    {
        ClientPatchState.Patched => "установлен",
        ClientPatchState.Original => "не установлен",
        ClientPatchState.Unknown => "файл не распознан",
        ClientPatchState.Unreadable => "файл не читается",
        _ => "файл не найден",
    };

    /// <summary>Подсказка к строке «Патч» при наведении: что значит состояние и что делать.</summary>
    public string PatchHint => Checking ? "Проверяю файл клиента" : State switch
    {
        ClientPatchState.Patched => "Правки стоят. Исходный файл сохранён рядом с exe как .orig — «Откат» вернёт его",
        ClientPatchState.Original => "Файл клиента исходный. Закройте игру и нажмите «Пропатчить»",
        ClientPatchState.Unknown => "Файл не похож ни на исходный, ни на пропатченный — скорее всего, игра обновилась. "
                                    + "Для нового билда нужен новый рецепт, файл бот не тронет",
        ClientPatchState.Unreadable => "Файл клиента не прочитать — не хватает прав на папку игры?",
        _ => "Файл клиента не найден: запустите игру и нажмите «Обновить» — или «Пропатчить» и укажите exe",
    };

    public ICommand PatchCommand { get; }
    public ICommand RestorePatchCommand { get; }

    public sealed record PatchPolicyChoice(ClientPatchPolicy Policy, string Title);

    public IReadOnlyList<PatchPolicyChoice> Policies { get; } =
    [
        new(ClientPatchPolicy.Ask, "Спросить"),
        new(ClientPatchPolicy.Require, "Не подключаться"),
        new(ClientPatchPolicy.Ignore, "Подключаться"),
    ];

    /// <summary>Подключение к клиенту без правок — на усмотрение пользователя (в settings.json).</summary>
    public ClientPatchPolicy PatchPolicy
    {
        get => Settings.PatchPolicy;
        set
        {
            if (Settings.PatchPolicy == value)
                return;

            Settings.PatchPolicy = value;
            OnPropertyChanged();
            _saved();
        }
    }

    private ConnectionSettings Settings => _settings();

    /// <summary>exe клиента этого сервера, запомненный от прошлых подключений и правок; null — не запоминали.</summary>
    private string? RememberedPath() => Settings.ClientPaths.TryGetValue(_profile().Id, out var remembered) ? remembered : null;

    /// <summary>Запомнить, где лежит exe клиента этого сервера, — пригодится, когда клиент не запущен.</summary>
    private void RememberPath(string path)
    {
        if (string.Equals(RememberedPath(), path, StringComparison.OrdinalIgnoreCase))
            return;

        Settings.ClientPaths[_profile().Id] = path;
        _saved();
    }

    /// <summary>
    /// Пустить ли подключение к клиенту: решение готовит <see cref="ClientPatchGate"/> (политика × состояние
    /// файла подключаемого — у Comeback два рабочих exe, проверяем именно его), здесь — показ вопроса/отказа и
    /// запоминание пути. false — не подключаемся.
    /// </summary>
    public bool AllowConnect(GameClient client)
    {
        if (Recipe is not { } recipe)
            return true;

        // Путь запоминаем до решения и при отказе тоже: «Требовать» отказывает со словами «закройте клиент и нажмите
        // «Пропатчить»» — а у закрытого клиента путь exe взять больше неоткуда
        if (client.ExePath is { } live)
            RememberPath(live);

        // Хэш здесь — в потоке окна: подключение и так ждёт ответа. Результат — строке патча (см. Refresh)
        var path = client.ExePath ?? RememberedPath();
        var state = ClientPatcher.Check(path, recipe);
        _justChecked = (path, state);

        var decision = ClientPatchGate.Decide(PatchPolicy, state);
        switch (decision.Verdict)
        {
            case ClientPatchVerdict.Allow:
                return true;

            case ClientPatchVerdict.Refuse:
                _log.Warning(decision.Problem);
                _dialogs.Tell(decision.Text);
                return false;

            default:
                _log.Warning(decision.Problem);
                return _dialogs.AskYesNo(decision.Text);
        }
    }

    // Что ворота подключения только что узнали о файле: ближайший Refresh для того же exe берёт это, а не считает
    // хэш второй раз. Только ближайший — потом файл мог смениться (правка, откат, обновление игры)
    private (string? Path, ClientPatchState State)? _justChecked;

    /// <summary>
    /// Пересчитать состояние для строки в окне. Хэш файла на десятки мегабайт — в фоне, окно не ждёт; только что
    /// проверенный воротами подключения exe второй раз не хэшируется. Вызывается после обновления списка клиентов,
    /// подключения, смены сервера, правки и отката.
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ShowPatch));
        var version = ++_checkVersion;
        var known = _justChecked;
        _justChecked = null;
        if (Recipe is not { } recipe)
            return;

        var path = ShownPath();
        if (known is { } fresh && string.Equals(fresh.Path, path, StringComparison.OrdinalIgnoreCase))
        {
            State = fresh.State;
            Checking = false;
            return;
        }

        Checking = true;
        Task.Run(() => ClientPatcher.Check(path, recipe))
            .ContinueWith(t => UiThread.Run(() =>
            {
                if (version != _checkVersion)
                    return;

                State = t.Result;
                Checking = false;
            }));
    }

    private void Patch()
    {
        if (Recipe is not { } recipe)
            return;

        var path = ShownPath() ?? AskClientPath();
        if (path is null || IsRunning(path))
            return;

        try
        {
            ClientPatcher.PatchFile(path, recipe, _log);
            RememberPath(path);
        }
        catch (ClientPatchException e)
        {
            _log.Error("Патч: " + e.Message);
            _dialogs.Tell(e.Message);
        }

        Refresh();
    }

    private void Restore()
    {
        if (Recipe is not { } recipe)
            return;

        var path = ShownPath() ?? AskClientPath();
        if (path is null || IsRunning(path))
            return;

        try
        {
            ClientPatcher.Restore(path, recipe, _log);
        }
        catch (ClientPatchException e)
        {
            _log.Error("Патч: " + e.Message);
            _dialogs.Tell(e.Message);
        }

        Refresh();
    }

    /// <summary>
    /// Чей exe показывать в строке: подключённого клиента (даже без ника), иначе — первого клиента с ником
    /// (ник читается — профиль подходит; список-то общий на все серверы), иначе — запомненный от подключений.
    /// </summary>
    private string? ShownPath()
    {
        var clients = _clients();
        var shown = (_connectedPid() is { } pid ? clients.FirstOrDefault(c => c.Pid == pid) : null)
                    ?? clients.FirstOrDefault(c => c.Nick is not null);
        return shown?.ExePath ?? RememberedPath();
    }

    /// <summary>
    /// Запущен ли клиент с этим exe — тогда файл не трогаем, а просим закрыть игру. Спрашиваем систему прямо сейчас,
    /// а не список в окне: тот обновляется по «Обновить», и клиент, закрытый после него, числился бы запущенным.
    /// </summary>
    private bool IsRunning(string path)
    {
        if (!SystemClientSource.Running().Any(c => string.Equals(c.ExePath, path, StringComparison.OrdinalIgnoreCase)))
            return false;

        _log.Warning($"Патч: клиент запущен — его exe занят ({path})");
        _dialogs.Tell("Клиент запущен — его exe занят. Закройте игру и повторите.");
        return true;
    }

    /// <summary>Где лежит exe клиента — спросить окном «Открыть», когда путь взять больше неоткуда.</summary>
    private string? AskClientPath()
        => _dialogs.AskOpenPath("Файл клиента игры (exe)", "Клиент игры (*.exe)|*.exe|Все файлы (*.*)|*.*");
}

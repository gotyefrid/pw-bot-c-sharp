using System;
using System.IO;
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
/// Строки «Игра» и «Патч» в карточке подключения: exe игры сервера, состояние файла, кнопки «Выбрать…»,
/// «Пропатчить» и «Откат». Патч — свойство установки игры (файла на диске), а не процесса, поэтому всё идёт от
/// пути «Игры» сервера (settings.json), а не от запущенного клиента. Решение «пускать ли к клиенту» — в
/// <see cref="ConnectCheck"/>. Зависимости — делегатами, как у соседних панелей.
/// </summary>
public sealed class ClientPatchPanel : ObservableObject
{
    private readonly Func<ServerProfile> _profile;
    private readonly Func<ConnectionSettings> _settings;
    private readonly Action _saved;
    private readonly IDialogs _dialogs;
    private readonly ILogger _log;

    /// <param name="settings">Настройки подключения: пути «Игры» по серверам.</param>
    /// <param name="saved">Настройки подключения изменены — сохранить.</param>
    /// <param name="dialogs">Выбор exe, сообщения об ошибках правки и занятом файле.</param>
    public ClientPatchPanel(
        Func<ServerProfile> profile,
        Func<ConnectionSettings> settings,
        Action saved,
        IDialogs dialogs,
        ILogger log)
    {
        _profile = profile;
        _settings = settings;
        _saved = saved;
        _dialogs = dialogs;
        _log = log;
        PatchCommand = new RelayCommand(Patch, () => Recipe is not null && !Checking && State is ClientPatchState.Original or ClientPatchState.Missing or ClientPatchState.Outdated);
        RestorePatchCommand = new RelayCommand(Restore, () => Recipe is not null && !Checking && State == ClientPatchState.Patched);
        ChooseGameCommand = new RelayCommand(ChooseGame, () => Recipe is not null);
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
        ClientPatchState.Outdated => "устарел",
        ClientPatchState.Unknown => "файл не распознан",
        ClientPatchState.Unreadable => "файл не читается",
        _ => "файл не найден",
    };

    /// <summary>Подсказка к строке «Патч» при наведении: что значит состояние и что делать.</summary>
    public string PatchHint => Checking ? "Проверяю файл клиента" : State switch
    {
        ClientPatchState.Patched => "Правки стоят. Исходный файл сохранён рядом с exe как .orig — «Откат» вернёт его",
        ClientPatchState.Original => "Файл клиента исходный. Закройте игру и нажмите «Пропатчить»",
        ClientPatchState.Outdated => "На диске правки прежней версии бота. Закройте игру и нажмите «Пропатчить» — "
                                     + "бот соберёт текущий патч из сохранённого оригинала .orig",
        ClientPatchState.Unknown => "Файл не похож ни на исходный, ни на пропатченный — скорее всего, игра обновилась. "
                                    + "Для нового билда нужен новый рецепт, файл бот не тронет",
        ClientPatchState.Unreadable => "Файл клиента не прочитать — не хватает прав на папку игры?",
        _ => "Файл клиента не найден — укажите exe кнопкой «Выбрать…» у строки «Игра»",
    };

    public ICommand PatchCommand { get; }
    public ICommand RestorePatchCommand { get; }

    public ICommand ChooseGameCommand { get; }

    private ConnectionSettings Settings => _settings();

    /// <summary>«Игра» сервера — exe клиента; null — не задана.</summary>
    public string? GamePath => Settings.ClientPaths.TryGetValue(_profile().Id, out var path) ? path : null;

    /// <summary>Что написано в строке «Игра»: путь или приглашение выбрать.</summary>
    public string GameText => GamePath ?? "не выбрана";

    /// <summary>«Игра» сменилась — список клиентов отбирается по ней заново.</summary>
    public event Action? GameChanged;

    /// <summary>
    /// Сделать этот exe «Игрой» сервера (угадана по запущенным клиентам, подключение, «Выбрать…», «Пропатчить»).
    /// Файл не проверяет — это дело вызывающего: в фоне (<see cref="Refresh"/>) или сразу перед подключением
    /// (<see cref="CheckNow"/>), чтобы не считать хэш дважды.
    /// </summary>
    public void Adopt(string path)
    {
        if (string.Equals(GamePath, path, StringComparison.OrdinalIgnoreCase))
            return;

        Settings.ClientPaths[_profile().Id] = path;
        _saved();
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(GameText));
        GameChanged?.Invoke();
    }

    private void ChooseGame()
    {
        if (AskClientPath() is not { } path)
            return;

        Adopt(path);
        Refresh();
    }

    /// <summary>
    /// Проверить файл «Игры» прямо сейчас, в потоке окна: «Подключить» и так ждёт ответа. Результат сразу
    /// попадает в строку «Патч» (отдельный пересчёт в фоне не нужен).
    /// </summary>
    public ClientPatchState CheckNow()
    {
        _checkVersion++;
        if (Recipe is not { } recipe)
            return ClientPatchState.Missing;

        State = ClientPatcher.Check(GamePath, recipe);
        Checking = false;
        return State;
    }

    /// <summary>
    /// Пересчитать состояние для строки в окне. Хэш файла на десятки мегабайт — в фоне, окно не ждёт.
    /// Вызывается при смене сервера и «Игры», после правки и отката.
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ShowPatch));
        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(GameText));
        var version = ++_checkVersion;
        if (Recipe is not { } recipe)
            return;

        var path = GamePath;
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

        // «Игры» нет или её файла уже нет (игру перенесли) — спросить, где exe теперь
        var path = GamePath is { } game && File.Exists(game) ? game : AskClientPath();
        if (path is null || IsRunning(path))
            return;

        try
        {
            ClientPatcher.PatchFile(path, recipe, _log);
            Adopt(path);
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

        var path = GamePath ?? AskClientPath();
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

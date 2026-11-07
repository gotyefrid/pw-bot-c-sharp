using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Notify;
using Notes.Core.Session;
using Notes.Core.Settings;

namespace Notes.App;

/// <summary>
/// Модель главного окна: вкладки персонажей (<see cref="CharacterTab"/>) и выбранная. У каждой вкладки своё подключение и
/// свой бот в своих потоках; окно показывает выбранную. Вкладка есть всегда: закрыли последнюю — на её месте новая.
/// Какие вкладки открыты — в tabs.json (<see cref="OpenTabsFile"/>): при запуске они открываются снова.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly WindowServices _services;
    private readonly IDialogs _dialogs;
    private readonly OpenTabsFile _remembered;
    private readonly TelegramPoller _commands;
    private CharacterTab _selected;
    private bool _loading = true;

    public MainViewModel(string appDirectory, IDialogs dialogs)
    {
        _services = new WindowServices(appDirectory);
        _dialogs = dialogs;
        _remembered = new OpenTabsFile(appDirectory);
        var (tabs, selected) = _remembered.Load(out var problem);
        foreach (var tab in tabs)
            New(tab);
        _selected = Tabs.Count > 0 ? Tabs[selected] : New(null);
        if (_services.Settings.Problem is { } settingsProblem)
            _selected.WindowLog.Warning(settingsProblem);
        if (problem is not null)
            _selected.WindowLog.Warning($"Список вкладок испорчен — открыта одна новая. {problem}");
        _loading = false;
        // Команды из Telegram — одни на окно; что не так — в лог «Чат» выбранной вкладки
        _commands = new TelegramPoller(() => _services.Settings.Value.ChatNotify, message => UiThread.Run(() => Answer(message)),
            text => UiThread.Run(() => Selected.ChatLog.Info(text)), text => UiThread.Run(() => Selected.ChatLog.Warning(text)));
        AddTabCommand = new RelayCommand(AddTab);
        CloseTabCommand = new RelayCommand<CharacterTab>(CloseTab);
    }

    public ObservableCollection<CharacterTab> Tabs { get; } = new();

    public CharacterTab Selected
    {
        get => _selected;
        set
        {
            if (value is not null && SetProperty(ref _selected, value))
                Remember();
        }
    }

    /// <summary>«+»: новая вкладка — пустая, как только что открытое окно бота.</summary>
    public ICommand AddTabCommand { get; }

    /// <summary>Крестик на вкладке: отключиться и закрыть (бот работает — сначала спросить).</summary>
    public ICommand CloseTabCommand { get; }

    /// <summary>Вкладка добавлена (окну — сделать ей вид).</summary>
    public event Action<CharacterTab>? TabAdded;

    /// <summary>Вкладка закрыта и освобождена (окну — убрать её вид).</summary>
    public event Action<CharacterTab>? TabClosed;

    /// <summary>Окно показано: первые списки клиентов во вкладках, вкладки прошлого запуска подключаются к своим.</summary>
    public void StartUp()
    {
        foreach (var tab in Tabs)
            tab.StartUp();
        _commands.Start();
    }

    // Сообщение из Telegram (в потоке окна): ответ — в тот же чат, ответом на него
    private void Answer(TelegramMessage message)
    {
        var connected = Tabs.Where(t => t.IsConnected && t.Nick is not null)
            .Select(t => new TelegramCharacter(t.Nick!, t.SelectedServer.Name, t.IsRunning)).ToList();
        var log = Selected.ChatLog;
        log.Info($"Из Telegram: {message.Text.Replace('\n', ' ')}");
        if (TelegramCommands.Answer(message, connected) is not { } reply)
            return;
        var s = _services.Settings.Value.ChatNotify;
        _services.Telegram.Post(s.Token, s.ChatId, reply, log, html: true, replyTo: message.MessageId);
    }

    private CharacterTab New(OpenTab? remembered)
    {
        var tab = new CharacterTab(_services, _dialogs, remembered);
        tab.PropertyChanged += TabChanged;
        Tabs.Add(tab);
        TabAdded?.Invoke(tab);
        Remember();
        return tab;
    }

    private void TabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CharacterTab.Remembered))
            Remember();
    }

    // Список вкладок — в tabs.json (при каждой перемене: окно может и упасть)
    private void Remember()
    {
        if (_loading || Tabs.Count == 0)
            return;
        if (_remembered.TrySave(Tabs.Select(t => t.Remembered).ToList(), Math.Max(0, Tabs.IndexOf(_selected))) is { } problem)
            _selected.WindowLog.Warning($"Не удалось запомнить вкладки: {problem}");
    }

    private void AddTab()
    {
        var tab = New(null);
        Selected = tab;
        tab.StartUp();
    }

    private void CloseTab(CharacterTab? tab)
    {
        if (tab is null || !Tabs.Contains(tab))
            return;
        if (tab.IsRunning && !_dialogs.AskYesNo($"Бот {tab.TabTitle} работает. Остановить его и закрыть вкладку?"))
            return;

        var index = Tabs.IndexOf(tab);
        if (Tabs.Count == 1)
            AddTab();
        else if (tab == Selected)
            Selected = Tabs[index == Tabs.Count - 1 ? index - 1 : index + 1];

        tab.PropertyChanged -= TabChanged;
        Tabs.Remove(tab);
        Remember();
        // Бот работал — его остановка дожидается в фоне, окно не ждёт
        _ = tab.CloseAsync();
        TabClosed?.Invoke(tab);
    }

    /// <summary>
    /// Окно закрывается: все вкладки отключаются разом, ждём их вместе (а не по 7 с на каждую), потом — общее окна. Список
    /// вкладок остаётся.
    /// </summary>
    public void Dispose()
    {
        _loading = true;
        _commands.Dispose();
        var closing = Tabs.Select(tab => tab.CloseAsync()).ToArray();
        Task.WaitAll(closing, ClientConnection.StopWait + TimeSpan.FromSeconds(1));
        _services.Dispose();
    }
}

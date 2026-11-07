using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Notes.App.Mvvm;

namespace Notes.App;

/// <summary>
/// Модель главного окна: вкладки персонажей (<see cref="CharacterTab"/>) и выбранная. У каждой вкладки своё подключение и
/// свой бот в своих потоках; окно показывает выбранную. Вкладка есть всегда: закрыли последнюю — на её месте новая.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly string _appDirectory;
    private readonly IDialogs _dialogs;
    private CharacterTab _selected;

    public MainViewModel(string appDirectory, IDialogs dialogs)
    {
        _appDirectory = appDirectory;
        _dialogs = dialogs;
        _selected = New();
        AddTabCommand = new RelayCommand(AddTab);
        CloseTabCommand = new RelayCommand<CharacterTab>(CloseTab);
    }

    public ObservableCollection<CharacterTab> Tabs { get; } = new();

    public CharacterTab Selected
    {
        get => _selected;
        set
        {
            if (value is not null)
                SetProperty(ref _selected, value);
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

    /// <summary>Окно показано: первые списки клиентов во вкладках.</summary>
    public void StartUp()
    {
        foreach (var tab in Tabs)
            tab.StartUp();
    }

    private CharacterTab New()
    {
        var tab = new CharacterTab(_appDirectory, _dialogs);
        Tabs.Add(tab);
        TabAdded?.Invoke(tab);
        return tab;
    }

    private void AddTab()
    {
        var tab = New();
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

        Tabs.Remove(tab);
        tab.Dispose();
        TabClosed?.Invoke(tab);
    }

    public void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Dispose();
    }
}

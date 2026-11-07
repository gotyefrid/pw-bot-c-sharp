using System;
using System.Collections.ObjectModel;
using Notes.App.Mvvm;

namespace Notes.App;

/// <summary>
/// Модель главного окна: вкладки персонажей (<see cref="CharacterTab"/>) и выбранная. У каждой вкладки своё подключение и
/// свой бот в своих потоках; окно показывает выбранную.
/// </summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private CharacterTab _selected;

    public MainViewModel(string appDirectory, IDialogs dialogs)
    {
        _selected = new CharacterTab(appDirectory, dialogs);
        Tabs.Add(_selected);
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

    /// <summary>Окно показано: первые списки клиентов во вкладках.</summary>
    public void StartUp()
    {
        foreach (var tab in Tabs)
            tab.StartUp();
    }

    public void Dispose()
    {
        foreach (var tab in Tabs)
            tab.Dispose();
    }
}

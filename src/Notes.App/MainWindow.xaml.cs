using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Media;

namespace Notes.App;

/// <summary>
/// Главное окно: полоса вкладок персонажей и вид выбранной. Вид (<see cref="CharacterView"/>) и значок на панели задач
/// (<see cref="TaskbarBadge"/>) — свои у каждой вкладки и живут, пока она открыта.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _host;
    private readonly ImageSource? _appIcon;
    private readonly Dictionary<CharacterTab, (CharacterView View, TaskbarBadge Badge)> _tabs = new();

    public MainWindow()
    {
        InitializeComponent();
        _appIcon = Icon;
        _host = new MainViewModel(AppDomain.CurrentDomain.BaseDirectory, new WindowDialogs(this));
        DataContext = _host;
        foreach (var tab in _host.Tabs)
            Added(tab);
        _host.TabAdded += Added;
        _host.TabClosed += Removed;
        _host.PropertyChanged += SelectionChanged;
        ShowSelected();
        // Первый список клиентов — когда окно уже показано; никуда не подключаемся, только «Подключить»
        Loaded += (_, _) => _host.StartUp();
    }

    private void Added(CharacterTab tab)
        => _tabs[tab] = (new CharacterView(tab), new TaskbarBadge(this, tab, _appIcon, shown: false));

    private void Removed(CharacterTab tab)
    {
        if (!_tabs.TryGetValue(tab, out var parts))
            return;
        parts.Badge.Dispose();
        _tabs.Remove(tab);
    }

    private void SelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Selected))
            ShowSelected();
    }

    private void ShowSelected()
    {
        foreach (var pair in _tabs)
            pair.Value.Badge.Shown = pair.Key == _host.Selected;
        if (_tabs.TryGetValue(_host.Selected, out var shown))
            TabHost.Content = shown.View;
    }

    /// <summary>Лог выбранной вкладки — для страховки выхода (<see cref="ExitWatchdog"/>).</summary>
    private Notes.Core.Logging.ILogger WindowLog => _host.Selected.WindowLog;

    protected override void OnClosed(EventArgs e)
    {
        var log = WindowLog;
        _host.Dispose();
        base.OnClosed(e);

        // Всё нужное уже сделано (боты отключены, точки сохранены): выход завис — страховка (см. ExitWatchdog)
        ExitWatchdog.Start(Dispatcher, Thread.CurrentThread, log);

        // Выход — на месте, без очереди. При «Закрыть окно» из панели задач (ПКМ по значку) поставленное в очередь до главного
        // потока не доходит: по логу страховки 04.10 главный поток ждал сообщений в GetMessage, диспетчер даже не начал выход,
        // Application.Shutdown и «толчок» так и стояли в очереди. InvokeShutdown из своего потока останавливает диспетчер сразу:
        // главный цикл выходит, когда этот обработчик закончится, — сообщение для этого не нужно
        Application.Current?.Shutdown();
        Dispatcher.InvokeShutdown();
    }
}

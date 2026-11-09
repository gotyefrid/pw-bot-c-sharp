using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Notes.App;

/// <summary>
/// Главное окно: полоса вкладок персонажей и вид выбранной. Вид (<see cref="CharacterView"/>) свой у каждой вкладки и живёт,
/// пока она открыта. Значок окна на панели задач — всегда один (app.ico).
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _host;
    private readonly Dictionary<CharacterTab, CharacterView> _views = new();

    public MainWindow()
    {
        InitializeComponent();
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

    private void Added(CharacterTab tab) => _views[tab] = new CharacterView(tab);

    private void SupportClick(object sender, RoutedEventArgs e) => SupportPopup.IsOpen = !SupportPopup.IsOpen;

    // Адрес кошелька — в буфер обмена; на кнопке на пару секунд «Скопировано»
    private void CopyWalletClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string address } button)
            return;
        try
        {
            Clipboard.SetText(address);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Буфер занят другой программой — адрес всё равно можно выделить в поле и скопировать
            return;
        }

        button.Content = "Скопировано";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            button.Content = "Копировать";
        };
        timer.Start();
    }

    private void Removed(CharacterTab tab) => _views.Remove(tab);

    // Двойной щелчок по вкладке — окно игры её персонажа сразу под окно бота; по крестику — нет (он закрывает вкладку)
    private void TabDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var at = e.OriginalSource as DependencyObject; at is not null && at != sender; at = at is Visual ? VisualTreeHelper.GetParent(at) : LogicalTreeHelper.GetParent(at))
        {
            if (at is ButtonBase)
                return;
        }

        if (sender is ListBoxItem { DataContext: CharacterTab tab })
        {
            tab.ShowGameWindow(new WindowInteropHelper(this).Handle);
            e.Handled = true;
        }
    }

    private void SelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Selected))
            ShowSelected();
    }

    private void ShowSelected()
    {
        if (_views.TryGetValue(_host.Selected, out var view))
            TabHost.Content = view;
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

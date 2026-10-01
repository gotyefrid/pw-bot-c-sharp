using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BotCH.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;
    private GlobalHotKeys? _hotKeys;

    public MainWindow()
    {
        InitializeComponent();
        _model = new MainViewModel(AppDomain.CurrentDomain.BaseDirectory);
        DataContext = _model;

        // Лог прокручивается к новой строке. Не сразу, а после того как список сам обработает добавление:
        // прокрутка внутри CollectionChanged идёт раньше списка — WPF видит рассогласование и падает
        _model.Log.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ScrollLogToEnd));
        };
    }

    // Любая правка в панели настроек: привязка уже записала значение в настройки — сохранить и отдать боту
    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            _model.SettingsEdited();
    }

    private void ScrollLogToEnd()
    {
        if (LogList.Items.Count > 0)
            LogList.ScrollIntoView(LogList.Items[LogList.Items.Count - 1]);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotKeys = new GlobalHotKeys(new WindowInteropHelper(this).Handle);
        var start = _hotKeys.Register(GlobalHotKeys.Control | GlobalHotKeys.Alt, GlobalHotKeys.NumPad1, _model.Start);
        var stop = _hotKeys.Register(GlobalHotKeys.Control | GlobalHotKeys.Alt, GlobalHotKeys.NumPad0, _model.Stop);
        _model.ReportHotKeys(start, stop);
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotKeys?.Dispose();
        _model.Dispose();
        base.OnClosed(e);
    }
}

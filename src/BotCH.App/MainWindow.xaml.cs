using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Media;
using BotCH.Core.Logging;
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

        // Лог в RichTextBox: текст можно выделять и копировать
        foreach (var entry in _model.Log)
            LogBox.Document.Blocks.Add(Line(entry));
        _model.Log.CollectionChanged += (_, e) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SyncLog(e)));
    }

    // Любая правка в панели настроек: привязка уже записала значение в настройки — сохранить и отдать боту
    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
            _model.SettingsEdited();
    }

    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x73, 0x85));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xD9, 0x8B, 0x0B));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));

    private void SyncLog(NotifyCollectionChangedEventArgs e)
    {
        var blocks = LogBox.Document.Blocks;
        if (e.Action == NotifyCollectionChangedAction.Add && e.NewItems is not null)
        {
            // Прокручиваем вниз, только если пользователь и так внизу (не мешаем выделять старое)
            var atEnd = LogBox.VerticalOffset + LogBox.ViewportHeight >= LogBox.ExtentHeight - 4;
            foreach (LogEntry entry in e.NewItems)
                blocks.Add(Line(entry));
            if (atEnd)
                LogBox.ScrollToEnd();
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && blocks.FirstBlock is not null)
        {
            blocks.Remove(blocks.FirstBlock);
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            blocks.Clear();
        }
    }

    private static Paragraph Line(LogEntry entry)
    {
        var line = new Paragraph { Margin = new Thickness(0, 0, 0, 1) };
        line.Inlines.Add(new Run($"{entry.Time:HH:mm:ss} [{entry.Source}] ") { Foreground = Muted });
        line.Inlines.Add(new Run(entry.Message)
        {
            Foreground = entry.Level switch
            {
                LogLevel.Warning => Warn,
                LogLevel.Error => Bad,
                LogLevel.Debug => Muted,
                _ => Brushes.Black,
            },
        });
        return line;
    }

    private void OpenLogFolder(object sender, RoutedEventArgs e)
    {
        var folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        System.IO.Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
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

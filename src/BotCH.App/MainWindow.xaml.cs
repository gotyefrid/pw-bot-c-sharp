using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using BotCH.Core.Logging;
using System.Windows;
using System.Windows.Threading;

namespace BotCH.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
    {
        InitializeComponent();
        _model = new MainViewModel(AppDomain.CurrentDomain.BaseDirectory)
        {
            AskYesNo = text => MessageBox.Show(this, text, "BotCH", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes,
            Tell = text => MessageBox.Show(this, text, "BotCH", MessageBoxButton.OK, MessageBoxImage.Error),
        };
        DataContext = _model;

        // Лог в RichTextBox: текст можно выделять и копировать
        RebuildLog();
        _model.Log.Entries.CollectionChanged += (_, e) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SyncLog(e)));
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
            {
                if (Shown(entry))
                    blocks.Add(Line(entry));
            }
            if (atEnd)
                LogBox.ScrollToEnd();
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems is not null)
        {
            // Строки в окне — не все записи (фильтр): удаляем, только если первая показанная — это удалённая запись
            foreach (LogEntry entry in e.OldItems)
            {
                if (ReferenceEquals(blocks.FirstBlock?.Tag, entry))
                    blocks.Remove(blocks.FirstBlock);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            blocks.Clear();
        }
    }

    private static Paragraph Line(LogEntry entry)
    {
        var line = new Paragraph { Margin = new Thickness(0, 0, 0, 1), Tag = entry };
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

    private bool Shown(LogEntry entry) => !_model.Log.OnlyImportantLog || entry.Level >= LogLevel.Warning;

    private void RebuildLog()
    {
        var blocks = LogBox.Document.Blocks;
        blocks.Clear();
        foreach (var entry in _model.Log.Entries.Where(Shown))
            blocks.Add(Line(entry));
        LogBox.ScrollToEnd();
    }

    private void LogFilterChanged(object sender, RoutedEventArgs e) => RebuildLog();

    private void OpenLogFolder(object sender, RoutedEventArgs e)
    {
        var folder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        System.IO.Directory.CreateDirectory(folder);
        System.Diagnostics.Process.Start("explorer.exe", folder);
    }

    protected override void OnClosed(EventArgs e)
    {
        _model.Dispose();
        base.OnClosed(e);
        Application.Current?.Shutdown();

        // Всё нужное уже сделано (бот отключён, точки сохранены): выход завис — страховка (см. ExitWatchdog)
        ExitWatchdog.Start(Dispatcher, Thread.CurrentThread, _model.WindowLog);
    }
}

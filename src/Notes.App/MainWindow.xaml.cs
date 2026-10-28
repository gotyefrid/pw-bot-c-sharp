using System;
using System.Collections.Specialized;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Controls;
using Notes.Core.Logging;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;

namespace Notes.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    public MainWindow()
    {
        InitializeComponent();
        Func<string, bool> askYesNo = text => MessageBox.Show(this, text, "Заметки", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        Action<string> tell = text => MessageBox.Show(this, text, "Заметки", MessageBoxButton.OK, MessageBoxImage.Error);
        _model = new MainViewModel(AppDomain.CurrentDomain.BaseDirectory) { AskYesNo = askYesNo, Tell = tell };
        var patch = _model.Patch;
        patch.AskYesNo = askYesNo;
        patch.Tell = tell;
        patch.AskClientPath = () => AskPath(new OpenFileDialog { Title = "Файл клиента игры (exe)", Filter = "Клиент игры (*.exe)|*.exe|Все файлы (*.*)|*.*" });
        // Сначала диалоги, потом первый список клиентов — и когда окно уже показано: вопрос о клиенте без правок
        // не появится раньше самого окна и не ответится заглушкой
        Loaded += (_, _) => _model.StartUp();
        DataContext = _model;

        var clicker = _model.Clicker;
        clicker.EditRequested += () => new StepWindow { Owner = this, DataContext = clicker }.ShowDialog();
        clicker.AskYesNo = askYesNo;
        clicker.Tell = tell;
        clicker.AskExportPath = () => AskPath(new SaveFileDialog { Title = "Экспорт сценария кликера", Filter = ClickerFiles, FileName = "Кликер.json" });
        clicker.AskImportPath = () => AskPath(new OpenFileDialog { Title = "Импорт сценария кликера", Filter = ClickerFiles });

        // Лог в RichTextBox: текст можно выделять и копировать
        RebuildLog();
        _model.Log.FilterChanged += RebuildLog;
        _model.Log.Entries.CollectionChanged += (_, e) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SyncLog(e)));
    }

    // Сценарий кликера в файл и из файла — обычные окна Windows «Сохранить как» и «Открыть»
    private const string ClickerFiles = "Сценарий кликера (*.json)|*.json|Все файлы (*.*)|*.*";

    private string? AskPath(FileDialog dialog) => dialog.ShowDialog(this) == true ? dialog.FileName : null;

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
            // Строки в окне — не все записи (фильтр), а урон и остальное вытесняются каждый своим: ищем строку удалённой записи
            foreach (LogEntry entry in e.OldItems)
            {
                if (blocks.FirstOrDefault(b => ReferenceEquals(b.Tag, entry)) is { } line)
                    blocks.Remove(line);
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

    private bool Shown(LogEntry entry) => _model.Log.Shows(entry);

    private void RebuildLog()
    {
        var blocks = LogBox.Document.Blocks;
        blocks.Clear();
        foreach (var entry in _model.Log.Entries.Where(Shown))
            blocks.Add(Line(entry));
        LogBox.ScrollToEnd();
    }

    private void OpenLogFilters(object sender, RoutedEventArgs e) => new LogFilterWindow { Owner = this, DataContext = _model.Log }.ShowDialog();

    // Двойной щелчок по строке (не по заголовку и не по пустому месту) — окно шага
    private void EditClickerStep(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is ListViewItem)
            _model.Clicker.Edit();
    }

    // Delete в списке шагов — удалить без вопросов; строки пересобраны — фокус на новую выбранную, чтобы жать дальше
    private void ClickerStepKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Delete || !_model.Clicker.RemoveCommand.CanExecute(null))
            return;

        e.Handled = true;
        _model.Clicker.RemoveCommand.Execute(null);
        var list = (ListView)sender;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) is ListViewItem item)
                item.Focus();
            else
                list.Focus();
        }));
    }

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

        // Всё нужное уже сделано (бот отключён, точки сохранены): выход завис — страховка (см. ExitWatchdog)
        ExitWatchdog.Start(Dispatcher, Thread.CurrentThread, _model.WindowLog);

        // Выход — на месте, без очереди. При «Закрыть окно» из панели задач (ПКМ по значку) поставленное в очередь до главного
        // потока не доходит: по логу страховки 04.10 главный поток ждал сообщений в GetMessage, диспетчер даже не начал выход,
        // Application.Shutdown и «толчок» так и стояли в очереди. InvokeShutdown из своего потока останавливает диспетчер сразу:
        // главный цикл выходит, когда этот обработчик закончится, — сообщение для этого не нужно
        Application.Current?.Shutdown();
        Dispatcher.InvokeShutdown();
    }
}

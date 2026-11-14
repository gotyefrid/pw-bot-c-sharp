using System;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Notes.Core.Logging;

namespace Notes.App;

/// <summary>
/// Вид вкладки персонажа: всё, что раньше было окном бота. Свой у каждой вкладки и живёт вместе с ней — лог в нём копится,
/// даже пока вкладка не выбрана (окно показывает только вид выбранной).
/// </summary>
public partial class CharacterView : UserControl
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x73, 0x85));
    private static readonly Brush Warn = new SolidColorBrush(Color.FromRgb(0xD9, 0x8B, 0x0B));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));

    private readonly CharacterTab _model;

    public CharacterView(CharacterTab model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;

        var clicker = model.Clicker;
        clicker.EditRequested += () => new StepWindow { Owner = Owner, DataContext = clicker }.ShowDialog();

        // Правка сохранённой точки (центр фарма, место для смертей)
        model.Farm.FarmCenter.EditRequested += EditPoint;
        model.Farm.DeathPoint.EditRequested += EditPoint;
        // Правка точки маршрута: координаты, «в воздухе», радиус и что копать
        model.Route.EditRequested += draft => new RoutePointWindow { Owner = Owner, DataContext = draft }.ShowDialog();
        model.Route.AskName = (title, current) => NameWindow.Ask(Owner, title, current);
        model.Combos.AskName = (title, current) => NameWindow.Ask(Owner, title, current);

        // Лог в RichTextBox: текст можно выделять и копировать
        RebuildLog();
        model.Log.FilterChanged += RebuildLog;
        model.Log.Reloaded += RebuildLog;
        model.Log.Entries.CollectionChanged += (_, e) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => SyncLog(e)));
    }

    public CharacterTab Model => _model;

    // Окно, в котором вкладка сейчас показана: хозяин диалогов
    private Window? Owner => Window.GetWindow(this) ?? Application.Current?.MainWindow;

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

    private void EditPoint(Panels.PointDraft draft) => new PointWindow { Owner = Owner, DataContext = draft }.ShowDialog();

    // Окно связок скиллов: пока оно открыто, вопросы, название связки и окно строки — поверх него
    private void OpenCombos(object sender, RoutedEventArgs e)
    {
        var combos = _model.Combos;
        var window = new ComboWindow { Owner = Owner, DataContext = combos };
        var dialogs = combos.Dialogs;
        var askName = combos.AskName;
        combos.Dialogs = new WindowDialogs(window);
        combos.AskName = (title, current) => NameWindow.Ask(window, title, current);
        combos.EditRequested = draft => new ComboRowWindow { Owner = window, DataContext = draft }.ShowDialog();
        combos.Observe();
        try
        {
            window.ShowDialog();
        }
        finally
        {
            combos.Dialogs = dialogs;
            combos.AskName = askName;
            combos.EditRequested = null;
        }
    }

    private void OpenLogFilters(object sender, RoutedEventArgs e) => new LogFilterWindow { Owner = Owner, DataContext = _model.Log }.ShowDialog();

    // «Персонажи»: список и правка. «Открыть» закрывает окно — запуск и вход идут во вкладке
    private void OpenCharacters(object sender, RoutedEventArgs e)
    {
        var window = new CharactersWindow { Owner = Owner };
        var panel = _model.CreateCharacters(new WindowDialogs(window));
        panel.EditRequested = draft => new CharacterWindow { Owner = window, DataContext = draft }.ShowDialog() == true;
        panel.Opened += window.Close;
        window.DataContext = panel;
        window.ShowDialog();
    }

    // Двойной щелчок по строке (не по заголовку и не по пустому месту) — окно шага
    private void EditClickerStep(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is ListViewItem)
            _model.Clicker.Edit();
    }

    // Двойной щелчок по точке маршрута (не по заголовку и не по пустому месту) — окно правки точки
    private void EditRoutePoint(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement((ItemsControl)sender, (DependencyObject)e.OriginalSource) is ListViewItem)
            _model.Route.EditSelected();
    }

    // Esc в таблице маршрута — снять выбор: «Старт» пойдёт с ближайшей точки
    private void RoutePointListKey(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape)
            return;
        _model.Route.ClearSelection();
        e.Handled = true;
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
}

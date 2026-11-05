using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Notes.Patcher;

/// <summary>
/// Окно патчера: раздел PWCLASSIC, выбор client.exe, список патчей и журнал.
/// Логика самих патчей — в <see cref="FlightPatch"/>; окно только показывает состояние и запускает действия.
/// </summary>
public partial class MainWindow : Window
{
    private static readonly Brush NormalBrush = CreateBrush(0x37, 0x41, 0x51);
    private static readonly Brush OkBrush = CreateBrush(0x18, 0x79, 0x4E);
    private static readonly Brush WarnBrush = CreateBrush(0xB4, 0x53, 0x09);
    private static readonly Brush ErrorBrush = CreateBrush(0xB4, 0x23, 0x18);
    private static readonly Brush MutedBrush = CreateBrush(0x6B, 0x72, 0x80);

    private readonly PatchItem _flightPatch;

    public MainWindow()
    {
        InitializeComponent();

        _flightPatch = new PatchItem(
            "Быстрый полёт",
            "Убирает разгон при полёте — скорость набирается сразу. Максимальная скорость не меняется: она зависит от надетого полётника.",
            ApplyFlight,
            RevertFlight);
        PatchesList.ItemsSource = new[] { _flightPatch };

        var near = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client.exe");
        if (File.Exists(near))
            SetClientPath(near);
        else
            RefreshState();
    }

    /// <summary>Выбрать клиент (из диалога, drag-and-drop или аргумента запуска).</summary>
    public void SetClientPath(string path)
    {
        ClientPathBox.Text = path;
        LogBox.Clear();
        Log("Клиент: " + path);
        RefreshState();
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите client.exe",
            Filter = "Клиент игры (client.exe)|client.exe|Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
            FilterIndex = 1,
        };

        var current = ClientPathBox.Text.Trim();
        var directory = current.Length > 0 && File.Exists(current) ? Path.GetDirectoryName(current) : null;
        dialog.InitialDirectory = directory ?? AppDomain.CurrentDomain.BaseDirectory;

        if (dialog.ShowDialog(this) == true)
            SetClientPath(dialog.FileName);
    }

    private void OnRefreshClick(object sender, RoutedEventArgs e) => RefreshState();

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            SetClientPath(files[0]);
    }

    /// <summary>Перепроверить выбранный файл и обновить строку патча.</summary>
    private void RefreshState()
    {
        var path = ClientPathBox.Text.Trim();
        if (path.Length == 0)
        {
            ShowClient("Клиент не выбран", "Нажмите «Выбрать…» или перетащите client.exe в окно.", MutedBrush);
            _flightPatch.SetState(PatchState.Unavailable, "недоступен", "—", false);
            return;
        }

        if (!File.Exists(path))
        {
            ShowClient("Файл не найден", path, ErrorBrush);
            _flightPatch.SetState(PatchState.Unavailable, "недоступен", "—", false);
            return;
        }

        FlightPatch.State state;
        string details;
        try
        {
            state = FlightPatch.Detect(path, out details);
        }
        catch (Exception e)
        {
            ShowClient("Не удалось проверить клиент", e.Message, ErrorBrush);
            _flightPatch.SetState(PatchState.Unavailable, "недоступен", "—", false);
            return;
        }

        switch (state)
        {
            case FlightPatch.State.Original:
                ShowClient("Оригинал — патч не применён", details, NormalBrush);
                _flightPatch.SetState(PatchState.NotApplied, "не применён", "Пропатчить", true);
                break;
            case FlightPatch.State.Patched:
                ShowClient("Пропатчен — быстрый полёт", details, OkBrush);
                _flightPatch.SetState(PatchState.Applied, "применён", "Откатить", true);
                break;
            case FlightPatch.State.Unknown:
                ShowClient("Клиент не узнан — возможно, обновился", details, WarnBrush);
                _flightPatch.SetState(PatchState.Unavailable, "недоступен", "—", false);
                break;
            case FlightPatch.State.NotClient:
                ShowClient("Это не client.exe", details, ErrorBrush);
                _flightPatch.SetState(PatchState.Unavailable, "недоступен", "—", false);
                break;
            case FlightPatch.State.Unreadable:
                ShowClient("Файл не читается", details, ErrorBrush);
                _flightPatch.SetState(PatchState.Unavailable, "недоступен", "—", false);
                break;
            default:
                ShowClient("Неизвестное состояние", details, WarnBrush);
                _flightPatch.SetState(PatchState.Unavailable, "недоступен", "—", false);
                break;
        }
    }

    private void ShowClient(string state, string details, Brush color)
    {
        ClientStateText.Text = state;
        ClientStateText.Foreground = color;
        ClientDetailsText.Text = details;
    }

    private void ApplyFlight() => RunPatch(() => FlightPatch.Apply(ClientPathBox.Text.Trim(), Log));

    private void RevertFlight() => RunPatch(() => FlightPatch.Revert(ClientPathBox.Text.Trim(), Log));

    /// <summary>Общий запуск патча/отката: блокирует кнопку, пишет журнал, обновляет состояние.</summary>
    private void RunPatch(Action patch)
    {
        _flightPatch.Busy = true;
        try
        {
            Log("---");
            patch();
        }
        catch (PatchException e)
        {
            Log("Ошибка: " + e.Message);
            MessageBox.Show(this, e.Message, "Патчер", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception e)
        {
            Log("Ошибка: " + e.Message);
            MessageBox.Show(this, e.Message, "Патчер", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _flightPatch.Busy = false;
            RefreshState();
        }
    }

    private void Log(string message)
    {
        LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogBox.ScrollToEnd();
    }

    private static Brush CreateBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

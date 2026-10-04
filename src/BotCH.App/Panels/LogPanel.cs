using System.Collections.ObjectModel;
using System.Windows.Input;
using BotCH.App.Mvvm;
using BotCH.Core.Logging;

namespace BotCH.App.Panels;

/// <summary>Лог в окне: последние строки, последняя запись для вкладки «Бот», фильтр «только важное».</summary>
public sealed class LogPanel : ObservableObject
{
    /// <summary>Сколько строк держать в окне (в файл пишется всё).</summary>
    public const int MaxLines = 500;

    private string _lastEvent = "";
    private bool _onlyImportantLog;

    public LogPanel() => ClearLogCommand = new RelayCommand(Entries.Clear);

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ICommand ClearLogCommand { get; }

    /// <summary>Последняя запись лога — видна на вкладке «Бот».</summary>
    public string LastEvent { get => _lastEvent; private set => SetProperty(ref _lastEvent, value); }

    /// <summary>В логе только предупреждения и ошибки.</summary>
    public bool OnlyImportantLog { get => _onlyImportantLog; set => SetProperty(ref _onlyImportantLog, value); }

    public void Add(LogEntry entry)
    {
        Entries.Add(entry);
        LastEvent = $"{entry.Time:HH:mm:ss} {entry.Message}";
        while (Entries.Count > MaxLines)
            Entries.RemoveAt(0);
    }
}

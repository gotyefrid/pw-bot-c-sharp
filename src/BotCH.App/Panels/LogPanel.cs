using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using BotCH.App.Mvvm;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.App.Panels;

/// <summary>Лог в окне: последние строки, последняя запись для вкладки «Бот», фильтры «только важное» и «урон».</summary>
public sealed class LogPanel : ObservableObject
{
    /// <summary>Сколько строк держать в окне — отдельно записей бота и урона (в файл пишется всё).</summary>
    public const int MaxLines = 500;

    private string _lastEvent = "";
    private bool _onlyImportantLog;
    private bool _showDamage;

    public LogPanel() => ClearLogCommand = new RelayCommand(Entries.Clear);

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ICommand ClearLogCommand { get; }

    /// <summary>Последняя запись лога — видна на вкладке «Бот».</summary>
    public string LastEvent { get => _lastEvent; private set => SetProperty(ref _lastEvent, value); }

    /// <summary>В логе только предупреждения и ошибки.</summary>
    public bool OnlyImportantLog { get => _onlyImportantLog; set => SetProperty(ref _onlyImportantLog, value); }

    /// <summary>В логе ещё и удары — кто кого и сколько (в файл пишутся всегда).</summary>
    public bool ShowDamage { get => _showDamage; set => SetProperty(ref _showDamage, value); }

    public static bool IsDamage(LogEntry entry) => entry.Source == DamageLog.Source;

    /// <summary>Показывать ли запись: урон — по галочке «Урон», остальное — по «только важное».</summary>
    public bool Shows(LogEntry entry) => IsDamage(entry) ? ShowDamage : !OnlyImportantLog || entry.Level >= LogLevel.Warning;

    public void Add(LogEntry entry)
    {
        Entries.Add(entry);
        var damage = IsDamage(entry);
        if (!damage)
            LastEvent = $"{entry.Time:HH:mm:ss} {entry.Message}";
        // Удары частые: вытесняют только старые удары, записи бота из окна не выталкивают
        if (Entries.Count(e => IsDamage(e) == damage) > MaxLines)
            Entries.Remove(Entries.First(e => IsDamage(e) == damage));
    }
}

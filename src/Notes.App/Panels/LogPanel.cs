using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Notes.App.Mvvm;
using Notes.Core.Logging;
using Notes.Core.Settings;

namespace Notes.App.Panels;

/// <summary>Лог в окне: последние строки, последняя запись для вкладки «Бот», фильтры — «только важное» и по источникам.</summary>
public sealed class LogPanel : ObservableObject
{
    /// <summary>Сколько строк держать в окне — отдельно записей бота и урона (в файл пишется всё).</summary>
    public const int MaxLines = 500;

    private string _lastEvent = "";
    private bool _onlyImportantLog;
    // Пока фильтр берётся из настроек — не сообщать о каждом переключателе
    private bool _loading;

    public LogPanel()
    {
        ClearLogCommand = new RelayCommand(Entries.Clear);
        Sources = LogSources.All.Select(s => new LogSourceFilter(s.Name, s.Hint)).ToList();
        foreach (var source in Sources)
            source.PropertyChanged += (_, _) => Changed();
    }

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public ICommand ClearLogCommand { get; }

    /// <summary>Последняя запись лога — видна на вкладке «Бот».</summary>
    public string LastEvent { get => _lastEvent; private set => SetProperty(ref _lastEvent, value); }

    /// <summary>В логе только предупреждения и ошибки (из включённых источников).</summary>
    public bool OnlyImportantLog
    {
        get => _onlyImportantLog;
        set
        {
            if (SetProperty(ref _onlyImportantLog, value))
                Changed();
        }
    }

    /// <summary>Переключатели по источникам — в окне фильтров.</summary>
    public IReadOnlyList<LogSourceFilter> Sources { get; }

    /// <summary>Фильтр поменяли: окно перестраивает лог, выбор сохраняется.</summary>
    public event Action? FilterChanged;

    private void Changed()
    {
        if (!_loading)
            FilterChanged?.Invoke();
    }

    /// <summary>Показывать ли запись. Источник не из списка (новый) — показывать.</summary>
    public bool Shows(LogEntry entry)
        => (!OnlyImportantLog || entry.Level >= LogLevel.Warning) && Sources.FirstOrDefault(s => s.Name == entry.Source) is not { Shown: false };

    public void Load(LogViewSettings settings)
    {
        _loading = true;
        OnlyImportantLog = settings.OnlyImportant;
        foreach (var source in Sources)
            source.Shown = !settings.Hidden.Contains(source.Name);
        _loading = false;
    }

    public void Save(LogViewSettings settings)
    {
        settings.OnlyImportant = OnlyImportantLog;
        settings.Hidden = Sources.Where(s => !s.Shown).Select(s => s.Name).ToList();
    }

    private static bool IsDamage(LogEntry entry) => entry.Source == LogSources.Damage;

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

/// <summary>Переключатель источника в окне фильтров лога.</summary>
public sealed class LogSourceFilter(string name, string hint) : ObservableObject
{
    private bool _shown = true;

    public string Name { get; } = name;
    public string Title { get; } = char.ToUpper(name[0]) + name.Substring(1);
    public string Hint { get; } = hint;
    public bool Shown { get => _shown; set => SetProperty(ref _shown, value); }
}

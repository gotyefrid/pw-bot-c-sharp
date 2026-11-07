using System;
using System.IO;

namespace Notes.Core.Settings;

/// <summary>
/// Общие настройки (settings.json в папке бота): подключение, уведомления в Telegram, что показывать в логе и шаблон для новых
/// персонажей. Один экземпляр на окно — все вкладки меняют его, правка одной сразу видна остальным (<see cref="Saved"/>).
/// Другие копии бота из той же папки по-прежнему не затираются: файл пишет <see cref="SettingsStore"/> со склейкой.
/// </summary>
public sealed class AppSettings
{
    private readonly SettingsStore _store;

    /// <param name="folder">Папка бота: settings.json и characters\.</param>
    public AppSettings(string folder)
    {
        Folder = folder;
        _store = new SettingsStore(System.IO.Path.Combine(folder, "settings.json"));
        Value = _store.Load(out var problem);
        Problem = problem;
    }

    /// <summary>Папка бота.</summary>
    public string Folder { get; }

    public BotSettings Value { get; }

    /// <summary>Почему файл не прочитан (взяты значения по умолчанию); null — прочитан или его не было.</summary>
    public string? Problem { get; }

    /// <summary>Сохранено: кто сохранил (вкладка, чтобы самой себе не перечитывать) — остальным обновить показанное.</summary>
    public event Action<object?>? Saved;

    /// <summary>В settings.json; не вышло — причина. <paramref name="by"/> — кто сохраняет (уйдёт в <see cref="Saved"/>).</summary>
    public string? Save(object? by = null)
    {
        try
        {
            _store.Save(Value);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
        Saved?.Invoke(by);
        return null;
    }
}

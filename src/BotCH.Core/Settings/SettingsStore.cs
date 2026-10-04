namespace BotCH.Core.Settings;

/// <summary>
/// Файл настроек рядом с exe. Сохранение атомарное (<see cref="JsonFile"/>): если бот упадёт посреди записи, останется
/// прежний целый файл.
/// </summary>
public sealed class SettingsStore(string path)
{
    public string Path { get; } = path;

    /// <summary>
    /// Загружает настройки. Файла нет — значения по умолчанию. Файл испорчен — копия <c>.bad</c> (чтобы не потерять),
    /// а бот стартует с настройками по умолчанию; причина — в <paramref name="problem"/>.
    /// </summary>
    public BotSettings Load(out string? problem)
    {
        var settings = JsonFile.Load(Path, SettingsJson.Parse, () => new BotSettings(), out var details);
        problem = details is null ? null : $"Файл настроек испорчен, взяты значения по умолчанию. {details}";
        return settings;
    }

    public void Save(BotSettings settings) => JsonFile.Save(Path, SettingsJson.Serialize(settings));
}

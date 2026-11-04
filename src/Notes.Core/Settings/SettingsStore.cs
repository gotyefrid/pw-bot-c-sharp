using System;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Notes.Core.Clients;

namespace Notes.Core.Settings;

/// <summary>
/// Файл настроек рядом с exe. Сохранение атомарное (<see cref="JsonFile"/>): если бот упадёт посреди записи, останется
/// прежний целый файл. Файл может быть открыт сразу в нескольких копиях бота (settings.json — общий у всех окон из одной папки):
/// сохранение пишет поверх файла на диске только то, что поменяли в этой копии с прошлого чтения или записи, — правки другой
/// копии не затираются.
/// </summary>
public sealed class SettingsStore(string path)
{
    // Сохранение длиннее миллисекунд — другая копия зависла; пишем без очереди
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    public string Path { get; } = path;

    // Каким настройки были в файле, когда эта копия последний раз читала или писала его; null — не читали, пишем всё
    private JObject? _baseline;

    /// <summary>
    /// Загружает настройки. Файла нет — значения по умолчанию. Файл испорчен — копия <c>.bad</c> (чтобы не потерять),
    /// а бот стартует с настройками по умолчанию; причина — в <paramref name="problem"/>.
    /// </summary>
    public BotSettings Load(out string? problem)
    {
        var settings = JsonFile.Load(Path, SettingsJson.Parse, () => new BotSettings(), out var details);
        problem = details is null ? null : $"Файл настроек испорчен, взяты значения по умолчанию. {details}";
        _baseline = JObject.Parse(SettingsJson.Serialize(settings));
        return settings;
    }

    public void Save(BotSettings settings)
    {
        var ours = JObject.Parse(SettingsJson.Serialize(settings));
        using var mutex = new Mutex(initiallyOwned: false, MutexNames.For("settings", System.IO.Path.GetFullPath(Path).ToLowerInvariant()));
        var owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(LockWait);
            }
            catch (AbandonedMutexException)
            {
                // Другая копия упала посреди записи: файл всё равно целый (запись атомарная)
                owned = true;
            }

            var text = _baseline is { } baseline && ReadDisk() is { } disk ? Merge(disk, baseline, ours) : ours;
            JsonFile.Save(Path, text.ToString(Formatting.Indented));
            _baseline = ours;
        }
        finally
        {
            if (owned)
                mutex.ReleaseMutex();
        }
    }

    // Файл на диске как JSON; нет или не разбирается — null (пишем своё целиком)
    private JObject? ReadDisk()
    {
        var disk = JsonFile.Load(Path, text => JObject.Parse(text), () => null!, out var problem);
        return problem is null ? disk : null;
    }

    /// <summary>
    /// Трёхсторонняя склейка: поле, которое эта копия поменяла (<paramref name="ours"/> не равно <paramref name="baseline"/>), —
    /// наше; не трогали — как в файле (<paramref name="disk"/>), там могла поменять другая копия. Вложенные объекты — по полям,
    /// списки и значения — целиком.
    /// </summary>
    public static JObject Merge(JObject disk, JObject baseline, JObject ours)
    {
        var merged = (JObject)disk.DeepClone();
        foreach (var property in ours.Properties())
        {
            var mine = property.Value;
            var was = baseline[property.Name];
            var there = merged[property.Name];
            if (was is not null && JToken.DeepEquals(was, mine))
            {
                if (there is null)
                    merged[property.Name] = mine.DeepClone();
                continue;
            }

            merged[property.Name] = mine is JObject m && was is JObject w && there is JObject t ? Merge(t, w, m) : mine.DeepClone();
        }

        return merged;
    }
}

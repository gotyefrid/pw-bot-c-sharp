using System;
using System.IO;
using System.Linq;

namespace BotCH.Core.Settings;

/// <summary>
/// Настройки по персонажам: <c>characters\Ник.json</c> рядом с exe. У каждого персонажа свои бой/цель/лут/банки/пет;
/// новый персонаж начинает с копии шаблона (общих настроек). Подключение (сервер, окна, unfreeze) — общее, не здесь.
/// </summary>
public sealed class CharacterSettings(string directory)
{
    public string Directory { get; } = directory;

    /// <summary>Файл персонажа. Символы, запрещённые в именах файлов, заменяются на «_».</summary>
    public string PathFor(string nick)
    {
        if (string.IsNullOrWhiteSpace(nick))
            throw new ArgumentException("Нет ника", nameof(nick));

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(nick.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(Directory, safe + ".json");
    }

    public bool Exists(string nick) => File.Exists(PathFor(nick));

    /// <summary>
    /// Настройки персонажа. Файла нет — копия <paramref name="template"/> (и файл создаётся сразу).
    /// Файл испорчен — копия <c>.bad</c> и шаблон, причина в <paramref name="problem"/>.
    /// </summary>
    public BotSettings Load(string nick, BotSettings template, out string? problem)
    {
        problem = null;
        var store = new SettingsStore(PathFor(nick));
        if (!File.Exists(store.Path))
        {
            var fresh = template.Clone();
            store.Save(fresh);
            return fresh;
        }

        var settings = store.Load(out problem);
        return problem is null ? settings : template.Clone();
    }

    public void Save(string nick, BotSettings settings) => new SettingsStore(PathFor(nick)).Save(settings);
}

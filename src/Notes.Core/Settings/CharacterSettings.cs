using System;
using System.IO;
using System.Linq;

namespace Notes.Core.Settings;

/// <summary>
/// Настройки по персонажам: <c>characters\&lt;сервер&gt;\Ник.json</c> рядом с exe (сервер — id профиля). Один ник на разных
/// серверах — разные персонажи. У каждого персонажа свои бой/цель/лут/банки/пет; новый персонаж начинает с копии шаблона
/// (общих настроек). Подключение (сервер, окна) — общее, не здесь.
/// </summary>
public sealed class CharacterSettings(string directory)
{
    public string Directory { get; } = directory;

    /// <summary>Файл персонажа. Символы, запрещённые в именах файлов, заменяются на «_».</summary>
    public string PathFor(string server, string nick)
        => Path.Combine(Directory, SafeName(server, nameof(server)), SafeName(nick, nameof(nick)) + ".json");

    /// <summary>Файл от прошлых версий бота — без папки сервера.</summary>
    public string OldPathFor(string nick) => Path.Combine(Directory, SafeName(nick, nameof(nick)) + ".json");

    /// <summary>Имя для файла или папки: без запрещённых символов и пробелов по краям.</summary>
    public static string SafeName(string name, string what = "name")
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Пустое имя", what);

        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    public bool Exists(string server, string nick) => File.Exists(PathFor(server, nick));

    /// <summary>
    /// Файл персонажа от прошлых версий (<c>characters\Ник.json</c>) — в папку сервера, если там его ещё нет.
    /// true — перенесли. Старый файл без сервера достаётся тому серверу, где персонаж подключился первым.
    /// </summary>
    public bool MoveOld(string server, string nick)
    {
        var old = OldPathFor(nick);
        var path = PathFor(server, nick);
        if (File.Exists(path) || !File.Exists(old))
            return false;

        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.Move(old, path);
            return true;
        }
        catch (IOException)
        {
            // Другая копия бота перенесла его только что
            return false;
        }
    }

    /// <summary>
    /// Настройки персонажа. Файла нет — копия <paramref name="template"/> (и файл создаётся сразу).
    /// Файл испорчен — копия <c>.bad</c> и шаблон, причина в <paramref name="problem"/>.
    /// </summary>
    public BotSettings Load(string server, string nick, BotSettings template, out string? problem)
    {
        problem = null;
        var store = new SettingsStore(PathFor(server, nick));
        if (!File.Exists(store.Path))
        {
            var fresh = template.Clone();
            store.Save(fresh);
            return fresh;
        }

        var settings = store.Load(out problem);
        return problem is null ? settings : template.Clone();
    }

    public void Save(string server, string nick, BotSettings settings) => new SettingsStore(PathFor(server, nick)).Save(settings);
}

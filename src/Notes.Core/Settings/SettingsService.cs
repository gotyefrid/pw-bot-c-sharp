using System;
using System.IO;
using Notes.Core.Logging;

namespace Notes.Core.Settings;

/// <summary>
/// Настройки бота на диске: общие (settings.json — подключение и шаблон для новых персонажей) и текущего персонажа
/// (characters\&lt;сервер&gt;\Ник.json). Пока персонаж неизвестен, «текущие» — это общие. Про подключение и бота ничего не знает:
/// окно само отдаёт новые настройки работающему боту. Общие (<see cref="AppSettings"/>) — одни у всех вкладок окна, настройки
/// персонажа — свои у вкладки.
/// </summary>
public sealed class SettingsService
{
    private readonly AppSettings _app;
    private readonly CharacterSettings _characters;
    private readonly ILogger _log;
    private readonly object? _owner;

    /// <param name="folder">Папка бота: settings.json и characters\. Общие настройки — свои (одна вкладка, тесты).</param>
    public SettingsService(string folder, ILogger log) : this(new AppSettings(folder), log)
    {
        if (_app.Problem is { } problem)
            log.Warning(problem);
    }

    /// <param name="app">Общие настройки окна (о том, что файл испорчен, пишет окно — один раз).</param>
    /// <param name="owner">Кто сохраняет общие (вкладка): <see cref="AppSettings.Saved"/> — с ним.</param>
    public SettingsService(AppSettings app, ILogger log, object? owner = null)
    {
        _app = app;
        _log = log;
        _owner = owner;
        Current = App;
        _characters = new CharacterSettings(Path.Combine(app.Folder, "characters"));
    }

    /// <summary>Общие: подключение и шаблон для новых персонажей.</summary>
    public BotSettings App => _app.Value;

    /// <summary>Настройки текущего персонажа; пока персонаж неизвестен — <see cref="App"/>.</summary>
    public BotSettings Current { get; private set; }

    /// <summary>Ник текущего персонажа; null — ещё неизвестен.</summary>
    public string? Character { get; private set; }

    /// <summary>Сервер текущего персонажа (id профиля); null — персонаж ещё неизвестен.</summary>
    public string? Server { get; private set; }

    /// <summary>
    /// Подключились к персонажу на сервере: берём его настройки (новый персонаж — копия общих), запоминаем его как последнего.
    /// false — это он и был, ничего не сменилось.
    /// </summary>
    public bool SwitchTo(string server, string nick)
    {
        if (nick == Character && server == Server)
            return false;

        if (App.Connection.LastCharacter != nick)
        {
            App.Connection.LastCharacter = nick;
            SaveApp();
        }

        if (_characters.MoveOld(server, nick))
            _log.Info($"Персонаж {nick}: настройки перенесены в папку сервера {server}");

        var isNew = !_characters.Exists(server, nick);
        Current = _characters.Load(server, nick, App, out var problem);
        Character = nick;
        Server = server;
        if (problem is not null)
            _log.Warning($"{nick}: {problem}");

        // Новый — сразу в свой файл: дальше он уже не копия общих
        if (isNew)
            SaveCurrent();

        _log.Info(isNew ? $"Персонаж {nick}: новые настройки (копия общих)" : $"Персонаж {nick}: его настройки загружены");
        return true;
    }

    /// <summary>Общие настройки — в settings.json.</summary>
    public void SaveApp()
    {
        if (_app.Save(_owner) is { } problem)
            _log.Error("Не удалось сохранить настройки: " + problem);
    }

    /// <summary>Настройки персонажа — в его файл; пока персонаж неизвестен — в общие.</summary>
    public void SaveCurrent()
    {
        if (Character is not { } nick || Server is not { } server)
        {
            SaveApp();
            return;
        }

        try
        {
            _characters.Save(server, nick, Current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Не удалось сохранить настройки {nick}: {e.Message}");
        }
    }
}

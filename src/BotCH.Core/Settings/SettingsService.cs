using System;
using System.IO;
using BotCH.Core.Logging;

namespace BotCH.Core.Settings;

/// <summary>
/// Настройки бота на диске: общие (settings.json — подключение и шаблон для новых персонажей) и текущего персонажа
/// (characters\Ник.json). Пока персонаж неизвестен, «текущие» — это общие. Про подключение и бота ничего не знает:
/// окно само отдаёт новые настройки работающему боту.
/// </summary>
public sealed class SettingsService
{
    private readonly SettingsStore _store;
    private readonly CharacterSettings _characters;
    private readonly ILogger _log;

    /// <param name="folder">Папка бота: settings.json и characters\.</param>
    public SettingsService(string folder, ILogger log)
    {
        _log = log;
        _store = new SettingsStore(Path.Combine(folder, "settings.json"));
        App = _store.Load(out var problem);
        Current = App;
        _characters = new CharacterSettings(Path.Combine(folder, "characters"));
        if (problem is not null)
            log.Warning(problem);
    }

    /// <summary>Общие: подключение и шаблон для новых персонажей.</summary>
    public BotSettings App { get; }

    /// <summary>Настройки текущего персонажа; пока персонаж неизвестен — <see cref="App"/>.</summary>
    public BotSettings Current { get; private set; }

    /// <summary>Ник текущего персонажа; null — ещё неизвестен.</summary>
    public string? Character { get; private set; }

    /// <summary>
    /// Подключились к персонажу: берём его настройки (новый персонаж — копия общих), запоминаем его как последнего.
    /// false — это он и был, ничего не сменилось.
    /// </summary>
    public bool SwitchTo(string nick)
    {
        if (nick == Character)
            return false;

        if (App.Connection.LastCharacter != nick)
        {
            App.Connection.LastCharacter = nick;
            SaveApp();
        }

        var isNew = !_characters.Exists(nick);
        Current = _characters.Load(nick, App, out var problem);
        Character = nick;
        if (problem is not null)
            _log.Warning($"{nick}: {problem}");

        // Unfreeze пишет в игру — у нового персонажа (и на другом сервере персонажи свои) выключен, пока не включат;
        // у персонажа из старого файла — как было общее
        if (isNew || Current.Unfreeze is null)
        {
            Current.Unfreeze = !isNew && App.Connection.Unfreeze;
            SaveCurrent();
        }

        _log.Info(isNew ? $"Персонаж {nick}: новые настройки (копия общих)" : $"Персонаж {nick}: его настройки загружены");
        return true;
    }

    /// <summary>Общие настройки — в settings.json.</summary>
    public void SaveApp()
    {
        try
        {
            _store.Save(App);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error("Не удалось сохранить настройки: " + e.Message);
        }
    }

    /// <summary>Настройки персонажа — в его файл; пока персонаж неизвестен — в общие.</summary>
    public void SaveCurrent()
    {
        if (Character is not { } nick)
        {
            SaveApp();
            return;
        }

        try
        {
            _characters.Save(nick, Current);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _log.Error($"Не удалось сохранить настройки {nick}: {e.Message}");
        }
    }
}

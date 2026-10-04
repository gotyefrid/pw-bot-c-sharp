using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.Core.Resources;

/// <summary>
/// Блокнот точек ресурсов вместе с файлами: общий файл точек (в него пишут все копии бота — перед сохранением сливаем с ним),
/// журнал событий (появился / выкопан / пропал), перенос старого файла из папки бота. Своих потоков нет: <see cref="Observe"/>
/// зовут на каждом снимке из потока снимков (не из окна — окну достаётся только последний снимок, а блокноту нужен каждый:
/// прыжок больше 30 м он считает телепортом, начало копки ловит по переходу «не копал → копает»). Окно читает точки и
/// сохраняет при закрытии из своего потока — поэтому всё под одним замком.
/// </summary>
public sealed class SpotService
{
    /// <summary>Сохранять не чаще, если что-то менялось.</summary>
    public static readonly TimeSpan SaveEvery = TimeSpan.FromSeconds(10);

    /// <summary>Подтягивать точки других копий бота, даже если у нас ничего не менялось.</summary>
    public static readonly TimeSpan SyncEvery = TimeSpan.FromMinutes(1);

    private readonly SpotBookStore _store;
    private readonly SpotBook _book;
    private readonly SpotJournal _journal;
    private readonly ILogger _log;
    private readonly Func<DateTime> _clock;
    private readonly object _lock = new();
    private DateTime _saved;
    private DateTime _synced;
    private string? _character;

    /// <param name="folder">Общая папка всех копий бота (%AppData%\BotCH): resources.json и resource-events.csv.</param>
    /// <param name="clock">Часы для «раз в 10 с / раз в минуту» (в тестах — подставные).</param>
    public SpotService(string folder, ILogger log, Func<DateTime>? clock = null)
    {
        _log = log;
        _clock = clock ?? (() => DateTime.UtcNow);
        _store = new SpotBookStore(Path.Combine(folder, "resources.json"));
        _book = new SpotBook(_store.Load(out var problem), log);
        if (problem is not null)
            log.Warning(problem);
        _book.Consolidate();
        _journal = new SpotJournal(Path.Combine(folder, "resource-events.csv"));
        _book.Happened += Write;
        _saved = _synced = _clock();
    }

    /// <summary>Точки сейчас (копия: блокнот тем временем меняется в потоке снимков).</summary>
    public IReadOnlyList<ResourceSpot> Spots
    {
        get
        {
            lock (_lock)
                return _book.Spots.ToList();
        }
    }

    /// <summary>Сервер, к которому подключены (id точек ресурсов у каждого сервера свои).</summary>
    public string Server
    {
        get
        {
            lock (_lock)
                return _book.Server;
        }
        set
        {
            lock (_lock)
                _book.Server = value;
        }
    }

    /// <summary>Новый снимок: блокнот смотрит, что появилось и пропало; пора — сохранить файл или подтянуть чужие точки.</summary>
    public void Observe(WorldState world)
    {
        lock (_lock)
        {
            // Чей снимок — того и события в журнале
            if (world.Host.Name.Length > 0)
                _character = world.Host.Name;
            _book.Observe(world);
            var now = _clock();
            if (now - _saved >= SaveEvery)
                SaveLocked(force: false);
            if (now - _synced >= SyncEvery)
            {
                _book.Merge(_store.Load(out _));
                _synced = now;
            }
        }
    }

    /// <summary>Мир не читается (другой персонаж, загрузка): что было видно — забыть.</summary>
    public void Forget()
    {
        lock (_lock)
            _book.Forget();
    }

    /// <summary>Сохранить, если что-то менялось (<paramref name="force"/> — в любом случае): сначала слить с файлом.</summary>
    public void Save(bool force = false)
    {
        lock (_lock)
            SaveLocked(force);
    }

    private void SaveLocked(bool force)
    {
        // Нечего сохранять — и часы не трогаем: иначе «подтянуть чужие точки раз в минуту» не наступило бы никогда
        if (!_book.Changed && !force)
            return;

        try
        {
            _book.Merge(_store.Load(out _));
            _store.Save(_book.Spots);
            _book.MarkSaved();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Файл занят другой копией — попробуем при следующем сохранении
            _log.Debug("Не удалось сохранить точки ресурсов: " + e.Message);
        }

        // Сохранение уже слило файл — это и подтягивание чужих точек
        _saved = _synced = _clock();
    }

    /// <summary>Точки, накопленные до общего файла в папке бота, — в общий файл; старый файл переименовывается.</summary>
    public void ImportOld(string file)
    {
        if (!File.Exists(file))
            return;

        var spots = new SpotBookStore(file).Load(out var problem);
        if (problem is not null)
        {
            _log.Warning(problem);
            return;
        }

        int added;
        lock (_lock)
        {
            added = _book.Merge(spots);
            SaveLocked(force: true);
        }
        try
        {
            File.Move(file, file + ".imported");
        }
        catch (IOException)
        {
            // Уже есть .imported — оставляем как есть, повторное слияние ничего не добавит
        }

        _log.Info($"Точки ресурсов из папки бота перенесены в общий файл {_store.Path}: новых {added}");
    }

    private void Write(SpotEvent e)
    {
        try
        {
            _journal.Write(e, _book.Server, _character);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Debug("Журнал ресурсов: " + ex.Message);
        }
    }
}

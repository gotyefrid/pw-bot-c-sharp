using System;
using System.Collections.Generic;
using System.IO;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.Core.Resources;

/// <summary>
/// Блокнот точек ресурсов вместе с файлами: общий файл точек (в него пишут все копии бота — перед сохранением сливаем с ним),
/// журнал событий (появился / выкопан / пропал), перенос старого файла из папки бота. Своих потоков нет: его зовут на каждом
/// снимке из одного потока (окна) — блокноту нужен каждый снимок (прыжок больше 30 м он считает телепортом, начало копки
/// ловит по переходу «не копал → копает»).
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
    private DateTime _saved;
    private DateTime _synced;

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

    public IReadOnlyList<ResourceSpot> Spots => _book.Spots;

    /// <summary>Сервер, к которому подключены (id точек ресурсов у каждого сервера свои).</summary>
    public string Server
    {
        get => _book.Server;
        set => _book.Server = value;
    }

    /// <summary>Чей персонаж сейчас — для журнала.</summary>
    public string? Character { get; set; }

    /// <summary>Новый снимок: блокнот смотрит, что появилось и пропало; пора — сохранить файл или подтянуть чужие точки.</summary>
    public void Observe(WorldState world)
    {
        _book.Observe(world);
        var now = _clock();
        if (now - _saved >= SaveEvery)
            Save();
        if (now - _synced >= SyncEvery)
        {
            _book.Merge(_store.Load(out _));
            _synced = now;
        }
    }

    /// <summary>Мир не читается (другой персонаж, загрузка): что было видно — забыть.</summary>
    public void Forget() => _book.Forget();

    /// <summary>Сохранить, если что-то менялось (<paramref name="force"/> — в любом случае): сначала слить с файлом.</summary>
    public void Save(bool force = false)
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

        var added = _book.Merge(spots);
        Save(force: true);
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
            _journal.Write(e, Server, Character);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Debug("Журнал ресурсов: " + ex.Message);
        }
    }
}

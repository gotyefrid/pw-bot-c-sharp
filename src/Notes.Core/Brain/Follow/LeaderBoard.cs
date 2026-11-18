using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.World;

namespace Notes.Core.Brain.Follow;

/// <summary>
/// Последний снимок каждого персонажа этого окна бота — по нику. Вкладка кладёт сюда каждый снимок своего клиента
/// (<see cref="Publish"/>), помощник в режиме «Следовать» смотрит, где главный и что он делает (<see cref="Find"/>). Одна на
/// процесс (<see cref="Shared"/>): главный — вкладка того же окна; копии бота в других процессах друг друга не видят.
/// Журнал заданий клиент читает не всегда (<see cref="WorldRequests.Quests"/>) — помощник просит его у главного
/// (<see cref="WantQuests"/>), вкладка главного включает чтение (<see cref="QuestsWanted"/>). Потокобезопасна: пишут потоки
/// снимков разных вкладок.
/// </summary>
public sealed class LeaderBoard
{
    /// <summary>Снимок старше этого — главного «не видно» (вкладку закрыли, клиент завис или отключён).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(3);

    /// <summary>Сколько держать просьбу читать журнал после последней (помощник просит её каждый ход).</summary>
    public static readonly TimeSpan QuestsWish = TimeSpan.FromSeconds(10);

    public static LeaderBoard Shared { get; } = new();

    private readonly object _lock = new();
    private readonly Dictionary<string, WorldState> _worlds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _questsWanted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Свежий снимок персонажа. Ника нет (ещё не вошёл в игру) — пропускаем.</summary>
    public void Publish(WorldState world)
    {
        if (world.Host.Name.Length == 0)
            return;
        lock (_lock)
            _worlds[world.Host.Name] = world;
    }

    /// <summary>Последний снимок персонажа <paramref name="nick"/>, если он не старше <see cref="Fresh"/> на момент <paramref name="now"/>; иначе null.</summary>
    public WorldState? Find(string nick, DateTime now)
    {
        lock (_lock)
            return _worlds.TryGetValue(nick.Trim(), out var world) && now - world.Time <= Fresh ? world : null;
    }

    /// <summary>Ники со свежими снимками — варианты «главного» для окна.</summary>
    public IReadOnlyList<string> Nicks(DateTime now)
    {
        lock (_lock)
            return _worlds.Values.Where(w => now - w.Time <= Fresh).Select(w => w.Host.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Помощнику нужен журнал заданий главного <paramref name="nick"/>.</summary>
    public void WantQuests(string nick, DateTime now)
    {
        lock (_lock)
            _questsWanted[nick.Trim()] = now;
    }

    /// <summary>Кто-то из помощников недавно (<see cref="QuestsWish"/>) просил журнал заданий персонажа <paramref name="nick"/>.</summary>
    public bool QuestsWanted(string nick, DateTime now)
    {
        lock (_lock)
            return _questsWanted.TryGetValue(nick, out var at) && now - at <= QuestsWish;
    }
}

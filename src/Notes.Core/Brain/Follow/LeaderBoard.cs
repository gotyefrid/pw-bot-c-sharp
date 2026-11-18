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
/// Награда на выбор: игра не говорит, какую выбрал игрок, — табло видит это по его сумке. Задание с выбором пропало из журнала
/// (сдал) — сравнивает сумку с той, что была до сдачи, пока не прибавятся предметы одного из вариантов (не дольше
/// <see cref="RewardWait"/>); узнанный номер — <see cref="RewardChoice"/>.
/// Скиллы: у персонажа появился скилл или вырос уровень — табло запоминает, у какого NPC (ближайший, не дальше
/// <see cref="TeacherNear"/>): <see cref="Teacher"/>. NPC рядом нет (скилл дала игра сама) — учитель неизвестен.
/// </summary>
public sealed class LeaderBoard
{
    /// <summary>Снимок старше этого — главного «не видно» (вкладку закрыли, клиент завис или отключён).</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(3);

    /// <summary>Сколько держать просьбу читать журнал после последней (помощник просит её каждый ход).</summary>
    public static readonly TimeSpan QuestsWish = TimeSpan.FromSeconds(10);

    /// <summary>Столько после сдачи ждём награду в сумке; не пришла (сумка полна) — выбор не узнан.</summary>
    public static readonly TimeSpan RewardWait = TimeSpan.FromSeconds(10);

    /// <summary>Учитель — NPC не дальше этого от персонажа, когда скилл вырос (говорить можно с ~4 м).</summary>
    public const float TeacherNear = 6f;

    public static LeaderBoard Shared { get; } = new();

    private readonly Dictionary<(string Nick, int Skill), int> _teachers = [];

    // Сдал задание с выбором — ждём награду в сумке: варианты, сумка до сдачи (tid → сколько), когда сдал
    private sealed record Handed(int Task, IReadOnlyList<IReadOnlyList<RewardItem>> Choices, Dictionary<uint, int> Before, DateTime At);

    private readonly Dictionary<string, List<Handed>> _handed = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Nick, int Task), int> _choices = [];

    private readonly object _lock = new();
    private readonly Dictionary<string, WorldState> _worlds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _questsWanted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Свежий снимок персонажа. Ника нет (ещё не вошёл в игру) — пропускаем.</summary>
    public void Publish(WorldState world)
    {
        if (world.Host.Name.Length == 0)
            return;
        lock (_lock)
        {
            if (_worlds.TryGetValue(world.Host.Name, out var was))
            {
                WatchRewards(was, world);
                WatchSkills(was, world);
            }
            _worlds[world.Host.Name] = world;
        }
    }

    /// <summary>
    /// Какую награду на выбор взял <paramref name="nick"/> за задание <paramref name="task"/> — номер с 0, как для сдачи; null — не
    /// узнали (ещё не сдал, награда не пришла, сумка полна).
    /// </summary>
    public int? RewardChoice(string nick, int task)
    {
        lock (_lock)
            return _choices.TryGetValue((nick.Trim().ToLowerInvariant(), task), out var choice) ? choice : null;
    }

    /// <summary>У какого NPC (tid) <paramref name="nick"/> последний раз учил или поднимал скилл <paramref name="skill"/>; null — не видели.</summary>
    public int? Teacher(string nick, int skill)
    {
        lock (_lock)
            return _teachers.TryGetValue((nick.Trim().ToLowerInvariant(), skill), out var npc) ? npc : null;
    }

    // Скилл появился или вырос: учитель — ближайший NPC рядом. Пустой список до или после — не прочитали, не сравниваем
    private void WatchSkills(WorldState was, WorldState now)
    {
        if (was.Skills.Count == 0 || now.Skills.Count == 0)
            return;

        foreach (var skill in now.Skills.Where(s => s.Level > (was.Skill(s.Id)?.Level ?? 0)))
        {
            var npc = now.Npcs.Where(n => n.Kind == NpcKind.Npc && n.Offset.Direct <= TeacherNear).OrderBy(n => n.Offset.Direct).FirstOrDefault();
            if (npc is { Tid: > 0 })
                _teachers[(now.Host.Name.ToLowerInvariant(), skill.Id)] = npc.Tid;
        }
    }

    private void WatchRewards(WorldState was, WorldState now)
    {
        var nick = now.Host.Name;
        if (!_handed.TryGetValue(nick, out var handed))
            _handed[nick] = handed = [];

        if (was.Quests is { } before && now.Quests is { } after)
            foreach (var quest in before.Where(q => q.RewardChoices is { Count: > 1 } && after.All(a => a.Id != q.Id)))
                handed.Add(new Handed(quest.Id, quest.RewardChoices!, Counts(was), now.Time));

        foreach (var h in handed.ToList())
        {
            var counts = Counts(now);
            var got = Enumerable.Range(0, h.Choices.Count)
                .Where(i => h.Choices[i].Count > 0 && h.Choices[i].All(r => Gain(h.Before, counts, r.Tid) >= Math.Max(1, r.Count)))
                .ToList();
            if (got.Count == 1)
                _choices[(nick.ToLowerInvariant(), h.Task)] = got[0];
            if (got.Count == 1 || now.Time - h.At > RewardWait)
                handed.Remove(h);
        }
    }

    private static int Gain(Dictionary<uint, int> before, Dictionary<uint, int> now, uint tid)
        => (now.TryGetValue(tid, out var n) ? n : 0) - (before.TryGetValue(tid, out var b) ? b : 0);

    private static Dictionary<uint, int> Counts(WorldState w)
        => w.Inventory.GroupBy(i => i.Tid).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));

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

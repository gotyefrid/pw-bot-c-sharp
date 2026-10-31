using System.Linq;
using System.Text.RegularExpressions;
using Notes.Core.Logging;

namespace Notes.Core.World;

/// <summary>
/// Кто убил персонажа — по фразе, которую игра пишет в чат после смерти («Itori. Запомните это имя. Так зовут вашего
/// убийцу.»). Работает и когда удара не видно: удар из инвиза в список ударов не попадает, а в чате убийца назван.
/// Смотрит только новые сообщения: что было в чате до подключения, не пишет.
/// </summary>
public sealed class KillerLog
{
    private readonly ILogger _log;
    private readonly Regex? _killedBy;
    private int? _last;

    /// <param name="killedBy">Фраза игры, «%s» — убийца (<see cref="Profiles.ChatOffsets.KilledBy"/>); пусто — не пишем.</param>
    public KillerLog(ILogger log, string killedBy)
    {
        _log = log;
        if (killedBy.Contains("%s"))
            _killedBy = new Regex("^" + Regex.Escape(killedBy).Replace("%s", "(.+)") + "$", RegexOptions.Compiled);
    }

    public void Observe(WorldState world)
    {
        if (_killedBy is null || world.Chat.Count == 0)
            return;

        var newest = world.Chat.Max(m => m.Number);
        // Первый снимок или номера пошли заново (перезаход в игру) — только запоминаем, где мы
        if (_last is not { } last || newest < last)
        {
            _last = newest;
            return;
        }

        foreach (var message in world.Chat.Where(m => m.Number > last))
        {
            if (_killedBy.Match(message.Plain) is { Success: true } match)
                _log.Warning($"Вас убил {Who(world, match.Groups[1].Value.Trim())}");
        }

        _last = newest;
    }

    // Игрок рядом — с классом и уровнем, если игра их знает; моб и остальные — как в чате
    private static string Who(WorldState w, string name)
    {
        if (w.Players.FirstOrDefault(p => p.Name == name) is not { } player)
            return name;

        var known = new[] { PlayerClasses.Name(player.Class), player.Level > 0 ? $"ур. {player.Level}" : null }.Where(s => s is not null).ToList();
        return known.Count == 0 ? $"игрок {name}" : $"игрок {name} ({string.Join(", ", known)})";
    }
}

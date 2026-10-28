using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.World;

/// <summary>
/// Выделяет новые удары из снимков: удар виден в списке игры несколько снимков подряд (0,4…2 с), а узел списка игра потом
/// отдаёт следующему удару. Тот же удар — в том же узле тот же, кто бьёт, тот же скилл и цели, и время в списке не уменьшилось.
/// Снимки нужны чаще, чем живёт удар (раз в 250 мс хватает), иначе часть ударов не попадёт ни в один.
/// </summary>
public sealed class AttackTracker
{
    private Dictionary<uint, AttackEvent> _seen = [];

    /// <summary>Удары, которых не было в прошлом снимке.</summary>
    public IReadOnlyList<AttackEvent> Update(WorldState world)
    {
        var fresh = new List<AttackEvent>();
        var seen = new Dictionary<uint, AttackEvent>();
        foreach (var attack in world.Attacks)
        {
            if (!_seen.TryGetValue(attack.Node, out var before) || !Same(before, attack))
                fresh.Add(attack);
            seen[attack.Node] = attack;
        }

        _seen = seen;
        return fresh;
    }

    private static bool Same(AttackEvent before, AttackEvent now)
        => before.AttackerWid == now.AttackerWid && before.Skill == now.Skill && now.LivedMs >= before.LivedMs
           && before.Hits.SequenceEqual(now.Hits);
}

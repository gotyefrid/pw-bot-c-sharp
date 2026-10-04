using System.Collections.Generic;
using System.Linq;

namespace BotCH.Core.World;

/// <summary>
/// Толкование мобов, которому нужна история снимков (читатель отдаёт только факты из памяти):
/// <list type="bullet">
/// <item>застрявшая цель — 1.3.6 после отагра цель не сбрасывает (1.4.6 — сбрасывает): видели, как моб пошёл назад
/// («возвращается»), — та же цель дальше застрявшая (0), пока он снова не ударит или не сменит цель: тогда это новый агр;</item>
/// <item>«идёт к нам» (<see cref="NpcInfo.Approaching"/>) — идёт и стал ближе по горизонтали, чем на прошлом снимке.</item>
/// </list>
/// Один на подключение, стоит между читателем и лентой снимков. Неудачный снимок (не в мире, перезаход) память не сбрасывает;
/// мобов, которых нет в новом снимке, забывает (WID моба после респавна тот же, но это уже другой бой).
/// </summary>
public sealed class NpcTracker
{
    // Моб отагрился (видели «возвращается») — какая цель у него тогда была
    private readonly Dictionary<uint, uint> _shakenOff = [];

    // Расстояние до персонажа (по горизонтали) на прошлом снимке
    private readonly Dictionary<uint, float> _lastDistance = [];

    /// <summary>Снимок с толкованием: застрявшая цель снята, «идёт к нам» проставлено.</summary>
    public WorldState Track(WorldState world)
    {
        var npcs = world.Npcs.Select(Track).ToList();
        Forget(_shakenOff, npcs);
        Forget(_lastDistance, npcs);
        // Мобы уже привязаны к персонажу этого снимка — with их привязку не трогает
        return world with { Npcs = npcs };
    }

    private NpcInfo Track(NpcInfo npc)
    {
        var target = npc.TargetWid;
        if (npc.Returning)
            _shakenOff[npc.Wid] = target;
        else if (_shakenOff.TryGetValue(npc.Wid, out var stuck))
        {
            if (target == stuck && npc.State is not (NpcInfo.StateAttacking or NpcInfo.StateCasting))
                target = 0;
            else
                _shakenOff.Remove(npc.Wid);
        }

        var distance = npc.Offset.Horizontal;
        var approaching = npc.State == NpcInfo.StateMoving && _lastDistance.TryGetValue(npc.Wid, out var last) && distance < last;
        _lastDistance[npc.Wid] = distance;

        return target == npc.TargetWid && approaching == npc.Approaching ? npc : npc with { TargetWid = target, Approaching = approaching };
    }

    private static void Forget<T>(Dictionary<uint, T> known, List<NpcInfo> npcs)
    {
        if (known.Count == 0)
            return;
        var present = new HashSet<uint>(npcs.Select(n => n.Wid));
        foreach (var wid in known.Keys.Where(k => !present.Contains(k)).ToList())
            known.Remove(wid);
    }
}

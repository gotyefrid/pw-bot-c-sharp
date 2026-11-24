using System.Collections.Generic;
using System.Linq;
using Notes.Core.World;

namespace Notes.Core.Brain;

/// <summary>
/// Кого пет уже ударил хотя бы раз (по списку ударов игры). Нужно для агра: моба, который бьёт пета, а пет его ещё не трогал,
/// любое лечение пета переводит на перса (у моба нет «обидчика» — цель берёт лекарь). Ударил пет — моб держится на нём.
/// Мобы, пропавшие из виду или умершие, забываются: возродившийся бывает с тем же WID.
/// </summary>
public sealed class PetTags
{
    private readonly HashSet<uint> _hit = [];

    public void Observe(WorldState w)
    {
        if (w.Pet is not { IsSummoned: true } pet)
        {
            _hit.Clear();
            return;
        }

        foreach (var attack in w.Attacks.Where(a => a.AttackerWid == pet.ActiveWid))
            foreach (var hit in attack.Hits)
                _hit.Add(hit.TargetWid);

        _hit.RemoveWhere(wid => w.Npcs.FirstOrDefault(n => n.Wid == wid) is not { IsDead: false });
    }

    public bool Hit(uint mobWid) => _hit.Contains(mobWid);

    public void Reset() => _hit.Clear();
}

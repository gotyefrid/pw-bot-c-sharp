using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.World;

/// <summary>
/// Толкование мобов, которому нужна история снимков (читатель отдаёт только факты из памяти):
/// <list type="bullet">
/// <item>застрявшая цель — 1.3.6 после отагра цель не сбрасывает (1.4.6 — сбрасывает): видели, как моб пошёл назад
/// («возвращается»), — та же цель дальше застрявшая (0), пока он снова не ударит или не сменит цель: тогда это новый агр;</item>
/// <item>кто из мобов напал на перса или пета (<see cref="NpcInfo.Aggro"/>) — см. ниже.</item>
/// </list>
/// <para>
/// Напал — только когда есть подтверждение: цель моба — мы (общее поле цели, а где его нет — цель удара или каста в момент
/// действия), или его удар по нам в списке ударов игры. Подтверждённую цель держим за мобом, пока он не умер, не ушёл домой
/// («возвращается»), не пропал из виду, не взял другую подтверждённую цель (игрока, чужого пета) — или пока <see cref="HoldFor"/>
/// нет новых подтверждений. Так моб, который стоит между ударами, или цель, мигнувшая на «никого» при смене пет ↔ перс, не
/// превращаются в «отстал». Моба, который только бежит к нам и ещё не ударил, не считаем: догадок по движению нет.
/// </para>
/// Один на подключение, стоит между читателем и лентой снимков. Неудачный снимок (не в мире, перезаход) память не сбрасывает;
/// мобов, которых нет в новом снимке, забывает (WID моба после респавна тот же, но это уже другой бой).
/// </summary>
public sealed class NpcTracker
{
    /// <summary>Сколько держим цель-«нас» после последнего подтверждения.</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(10);

    private sealed record Hold(uint Victim, DateTime Seen);

    // Моб отагрился (видели «возвращается») — какая цель у него тогда была
    private readonly Dictionary<uint, uint> _shakenOff = [];

    // Моб напал на перса или пета: на кого, когда последний раз подтверждалось
    private readonly Dictionary<uint, Hold> _held = [];

    /// <summary>Снимок с толкованием: застрявшая цель снята, нападения проставлены.</summary>
    public WorldState Track(WorldState world)
    {
        var scene = new Scene(world);
        var npcs = world.Npcs.Select(n => Track(n, scene)).ToList();
        Forget(_shakenOff, npcs);
        Forget(_held, npcs);
        // Мобы уже привязаны к персонажу этого снимка — with их привязку не трогает
        return world with { Npcs = npcs };
    }

    private NpcInfo Track(NpcInfo npc, Scene scene)
    {
        var target = npc.TargetWid;
        if (npc.Returning)
            _shakenOff[npc.Wid] = target;
        else if (_shakenOff.TryGetValue(npc.Wid, out var stuck))
        {
            if (target == stuck && npc.State is not (NpcState.Attacking or NpcState.Casting))
                target = 0;
            else
                _shakenOff.Remove(npc.Wid);
        }

        var (victim, sign) = npc.Kind == NpcKind.Mob ? Aggro(npc, target, scene) : (target, AggroSign.None);
        return victim == npc.TargetWid && sign == npc.Aggro ? npc : npc with { TargetWid = victim, Aggro = sign };
    }

    // Кого из нас бьёт моб и почему; не нас — его цель как есть
    private (uint Victim, AggroSign Sign) Aggro(NpcInfo mob, uint target, Scene scene)
    {
        var wid = mob.Wid;
        if (mob.IsDead || mob.Returning)
        {
            _held.Remove(wid);
            return (target, AggroSign.None);
        }

        // Подтверждения: цель — мы; удар по нам в списке ударов
        if (scene.IsOurs(target))
            return Confirm(wid, target, mob.State is NpcState.Attacking or NpcState.Casting ? AggroSign.Action : AggroSign.Target, scene);
        if (scene.HitBy(wid) is { } hit)
        {
            if (scene.IsOurs(hit))
                return Confirm(wid, hit, AggroSign.Hit, scene);
            // Бьёт кого-то другого — сменил цель
            _held.Remove(wid);
            return (target, AggroSign.None);
        }

        // Цель — другой, кого мы видим (игрок, чужой пет, NPC): сменил цель
        if (scene.IsSomeoneElse(target, wid))
        {
            _held.Remove(wid);
            return (target, AggroSign.None);
        }

        // Подтверждения нет — держим прежнее, пока не истекло
        if (_held.TryGetValue(wid, out var hold))
        {
            if (scene.IsOurs(hold.Victim) && scene.Time - hold.Seen <= HoldFor)
                return (hold.Victim, AggroSign.Held);
            _held.Remove(wid);
        }

        return (target, AggroSign.None);
    }

    private (uint, AggroSign) Confirm(uint wid, uint victim, AggroSign sign, Scene scene)
    {
        _held[wid] = new Hold(victim, scene.Time);
        return (victim, sign);
    }

    private static void Forget<T>(Dictionary<uint, T> known, List<NpcInfo> npcs)
    {
        if (known.Count == 0)
            return;
        var present = new HashSet<uint>(npcs.Select(n => n.Wid));
        foreach (var wid in known.Keys.Where(k => !present.Contains(k)).ToList())
            known.Remove(wid);
    }

    // Что нужно знать о снимке, чтобы толковать мобов: кто мы, кто ещё вокруг, кто кого бьёт по списку ударов
    private sealed class Scene
    {
        private readonly uint _host;
        private readonly uint _pet;
        private readonly HashSet<uint> _others;
        private readonly Dictionary<uint, uint> _hits = [];

        public Scene(WorldState w)
        {
            Time = w.Time;
            _host = w.Host.Wid;
            _pet = w.Pet is { IsSummoned: true } p ? p.ActiveWid : 0;
            _others = new HashSet<uint>(w.Players.Select(p => p.Wid).Concat(w.Npcs.Select(n => n.Wid)));
            _others.Remove(_host);
            _others.Remove(_pet);

            // Последний удар моба: по нам — главнее (если в одном снимке и по нам, и по другому)
            foreach (var attack in w.Attacks)
            {
                foreach (var hit in attack.Hits)
                {
                    if (IsOurs(hit.TargetWid) || !_hits.ContainsKey(attack.AttackerWid))
                        _hits[attack.AttackerWid] = hit.TargetWid;
                }
            }
        }

        public DateTime Time { get; }

        public bool IsOurs(uint wid) => wid != 0 && (wid == _host || wid == _pet);

        public bool IsSomeoneElse(uint wid, uint self) => wid != self && _others.Contains(wid);

        /// <summary>Кого моб ударил по списку ударов игры (живёт 0,4…2 с); null — не бил.</summary>
        public uint? HitBy(uint mob) => _hits.TryGetValue(mob, out var whom) ? whom : null;
    }
}

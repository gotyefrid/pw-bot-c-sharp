using System;
using System.Collections.Generic;
using System.Linq;

namespace Notes.Core.World;

/// <summary>
/// Толкование мобов, которому нужна история снимков (читатель отдаёт только факты из памяти):
/// <list type="bullet">
/// <item>застрявшая цель — 1.3.6 после отагра цель не сбрасывает (1.4.6 — сбрасывает): видели, как моб пошёл назад
/// («возвращается»), — та же цель дальше застрявшая (0), пока он снова не ударит или не сменит цель: тогда это новый агр;</item>
/// <item>«идёт к нам» (<see cref="NpcInfo.Approaching"/>) — идёт и стал ближе по горизонтали, чем на прошлом снимке;</item>
/// <item>кто из мобов напал на перса или пета (<see cref="NpcInfo.Aggro"/>) — см. ниже.</item>
/// </list>
/// <para>
/// Напал — когда есть подтверждение: цель моба — мы (общее поле цели, а где его нет — цель удара или каста в момент
/// действия), или его удар по нам в списке ударов игры. Подтверждённую цель держим за мобом, пока он не умер, не ушёл домой
/// («возвращается»), не пропал из виду, не взял другую подтверждённую цель (игрока, чужого пета) — или пока
/// <see cref="HoldFor"/> нет ни подтверждений, ни боевой стойки. Так моб, который стоит между ударами, или цель, мигнувшая
/// на «никого» при смене пет ↔ перс, не превращаются в «отстал».
/// </para>
/// <para>
/// Без подтверждения — догадка по анимации: моб в боевой стойке или бежит, а вокруг нет других игроков и чужих петов — значит,
/// бой с нами (с персом или петом — кто к нему ближе). Это единственное, что видно до первого удара там, где общего поля цели
/// нет (PW Classic). После отагра догадку не делаем, пока моб не успокоится (встанет или пойдёт шагом).
/// </para>
/// Один на подключение, стоит между читателем и лентой снимков. Неудачный снимок (не в мире, перезаход) память не сбрасывает;
/// мобов, которых нет в новом снимке, забывает (WID моба после респавна тот же, но это уже другой бой).
/// </summary>
public sealed class NpcTracker
{
    /// <summary>Сколько держим цель-«нас» без подтверждений и без боевой стойки.</summary>
    public static readonly TimeSpan HoldFor = TimeSpan.FromSeconds(10);

    /// <summary>Сколько держим догадку по стойке, когда стойка и бег пропали (анимация мигает между действиями).</summary>
    public static readonly TimeSpan PoseGrace = TimeSpan.FromSeconds(1.5);

    /// <summary>
    /// Догадку по стойке делаем только ближе этого (по горизонтали, м): дальше моб может драться с игроком, которого мы ещё не
    /// видим, а мобы агрятся и бегут к нам с меньшего расстояния.
    /// </summary>
    public const float PoseRadius = 40;

    private sealed record Hold(uint Victim, DateTime Seen, AggroSign Sign);

    // Моб отагрился (видели «возвращается») — какая цель у него тогда была
    private readonly Dictionary<uint, uint> _shakenOff = [];

    // Расстояние до персонажа (по горизонтали) на прошлом снимке
    private readonly Dictionary<uint, float> _lastDistance = [];

    // Моб напал на перса или пета: на кого, когда последний раз подтверждалось
    private readonly Dictionary<uint, Hold> _held = [];

    // Видели «возвращается», а моб ещё не успокоился: его стойка и бег — не догадка о нападении
    private readonly Dictionary<uint, bool> _cooling = [];

    /// <summary>Снимок с толкованием: застрявшая цель снята, «идёт к нам» и нападения проставлены.</summary>
    public WorldState Track(WorldState world)
    {
        var scene = new Scene(world);
        var npcs = world.Npcs.Select(n => Track(n, scene)).ToList();
        Forget(_shakenOff, npcs);
        Forget(_lastDistance, npcs);
        Forget(_held, npcs);
        Forget(_cooling, npcs);
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

        var distance = npc.Offset.Horizontal;
        var approaching = npc.State == NpcState.Moving && _lastDistance.TryGetValue(npc.Wid, out var last) && distance < last;
        _lastDistance[npc.Wid] = distance;

        var (victim, sign) = npc.Kind == NpcKind.Mob ? Aggro(npc, target, scene) : (target, AggroSign.None);
        return victim == npc.TargetWid && approaching == npc.Approaching && sign == npc.Aggro
            ? npc
            : npc with { TargetWid = victim, Approaching = approaching, Aggro = sign };
    }

    // Кого из нас бьёт моб и почему; не нас — его цель как есть
    private (uint Victim, AggroSign Sign) Aggro(NpcInfo mob, uint target, Scene scene)
    {
        var wid = mob.Wid;
        if (mob.IsDead || mob.Returning)
        {
            _held.Remove(wid);
            if (mob.Returning)
                _cooling[wid] = true;
            return (target, AggroSign.None);
        }

        if (mob.Pose is NpcPose.Standing or NpcPose.Walking)
            _cooling.Remove(wid);

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

        // Подтверждения нет — держим прежнее, пока не истекло (стойка и бег продлевают: моб ещё в бою)
        if (_held.TryGetValue(wid, out var hold))
        {
            var fresh = mob.InCombatPose ? scene.Time : hold.Seen;
            var limit = hold.Sign == AggroSign.Pose ? PoseGrace : HoldFor;
            if (scene.IsOurs(hold.Victim) && scene.Time - fresh <= limit)
            {
                if (fresh != hold.Seen)
                    _held[wid] = hold with { Seen = fresh };
                return (hold.Victim, hold.Sign == AggroSign.Pose ? AggroSign.Pose : AggroSign.Held);
            }

            _held.Remove(wid);
        }

        // Догадка по анимации: в бою, а драться вокруг не с кем, кроме нас
        if (mob.InCombatPose && scene.Alone && !_cooling.ContainsKey(wid) && mob.Offset.Horizontal <= PoseRadius)
        {
            var guess = scene.Nearest(mob);
            _held[wid] = new Hold(guess, scene.Time, AggroSign.Pose);
            return (guess, AggroSign.Pose);
        }

        return (target, AggroSign.None);
    }

    private (uint, AggroSign) Confirm(uint wid, uint victim, AggroSign sign, Scene scene)
    {
        _held[wid] = new Hold(victim, scene.Time, sign);
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
        private readonly NpcInfo? _petNpc;
        private readonly HashSet<uint> _others;
        private readonly Dictionary<uint, uint> _hits = [];

        public Scene(WorldState w)
        {
            Time = w.Time;
            _host = w.Host.Wid;
            _pet = w.Pet is { IsSummoned: true } p ? p.ActiveWid : 0;
            _petNpc = _pet == 0 ? null : w.Npcs.FirstOrDefault(n => n.Wid == _pet);
            _others = new HashSet<uint>(w.Players.Select(p => p.Wid).Concat(w.Npcs.Select(n => n.Wid)));
            _others.Remove(_host);
            _others.Remove(_pet);
            // Вокруг никого: ни игроков, ни чужих петов (свой пет — это мы)
            Alone = w.Players.Count == 0 && !w.Npcs.Any(n => n.Kind == NpcKind.Pet && n.Wid != _pet);

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

        public bool Alone { get; }

        public bool IsOurs(uint wid) => wid != 0 && (wid == _host || wid == _pet);

        public bool IsSomeoneElse(uint wid, uint self) => wid != self && _others.Contains(wid);

        /// <summary>Кого моб ударил по списку ударов игры (живёт 0,4…2 с); null — не бил.</summary>
        public uint? HitBy(uint mob) => _hits.TryGetValue(mob, out var whom) ? whom : null;

        /// <summary>Кто из нас ближе к мобу — к тому и бежит догадка по стойке.</summary>
        public uint Nearest(NpcInfo mob)
        {
            if (_petNpc is null)
                return _host;
            var toPet = Offset.Between(_petNpc.Position, mob.Position).Horizontal;
            return toPet < mob.Offset.Horizontal ? _pet : _host;
        }
    }
}

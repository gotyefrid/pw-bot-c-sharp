using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.World;

namespace Notes.Core.Stats;

/// <summary>
/// События статистики из того, что видно в снимках мира. Поведения о статистике не знают: мозг (<see cref="Brain.BotBrain"/>)
/// после каждого хода отдаёт сюда снимок и две вещи, которых в снимке нет, — идёт ли слив опыта и ждёт ли бот подбора или копания.
/// <list type="bullet">
/// <item>Сессия: первый снимок — начало, <see cref="End"/> (бот остановлен) — конец; раз в <see cref="AlivePeriod"/> — «работает».</item>
/// <item>Убит: моб, который был в цели у перса или пета или бил их, умер у нас на глазах. Кто добил, в снимке не видно; мобов,
/// которых не трогали ни мы, ни пет, не считаем.</item>
/// <item>Подобрано и выкопано — прибавка в сумке и денег, пока бот ждёт подбора или копания и ещё <see cref="CollectGrace"/>
/// после: покупка, банк и награда за задание сюда не попадают.</item>
/// <item>Смерть перса — кроме слива опыта (там смерти нарочно). Слив — начало и конец.</item>
/// <item>Опыт — только прибавка (на новом уровне — добор до уровня и опыт на новом); уровень — каждый новый.</item>
/// <item>Ремонт — у надетых вещей выросла прочность (сама она только падает).</item>
/// </list>
/// </summary>
public sealed class StatsWatcher
{
    /// <summary>Как часто писать «бот работает».</summary>
    public static readonly TimeSpan AlivePeriod = TimeSpan.FromMinutes(5);

    /// <summary>Сколько ещё после подбора или копания считать прибавку в сумке добычей: предмет приходит чуть позже.</summary>
    public static readonly TimeSpan CollectGrace = TimeSpan.FromSeconds(3);

    // Моб был «наш» не дольше этого назад: цель у перса снимается чуть раньше, чем у моба ставится флаг смерти
    private static readonly TimeSpan EngagedMemory = TimeSpan.FromSeconds(10);

    // Один ремонт обновляет вещи не в одном снимке — прибавка прочности в эти секунды тот же ремонт
    private static readonly TimeSpan RepairMerge = TimeSpan.FromSeconds(10);

    private readonly Dictionary<uint, (string Name, DateTime At)> _engaged = [];
    private WorldState? _last;
    private bool _draining;
    private DateTime _aliveAt;
    private DateTime _collectUntil = DateTime.MinValue;
    private DateTime _repairedAt = DateTime.MinValue;

    /// <summary>Снимок после хода мозга.</summary>
    /// <param name="draining">Идёт слив опыта.</param>
    /// <param name="collecting">Бот ждёт подбора или копания.</param>
    public IReadOnlyList<StatEvent> Observe(WorldState now, bool draining, bool collecting)
    {
        var events = new List<StatEvent>();
        var time = now.Time;
        void Add(StatKind kind, string name = "", long amount = 1) => events.Add(new StatEvent(time, kind, name, amount));

        if (collecting)
            _collectUntil = time + CollectGrace;

        if (_last is not { } last)
        {
            Add(StatKind.SessionStart);
            _aliveAt = time;
        }
        else
        {
            if (time - _aliveAt >= AlivePeriod)
            {
                Add(StatKind.Alive);
                _aliveAt = time;
            }

            if (last.Host is { IsDead: false } && now.Host is { IsDead: true, MaxHp: > 0 } && !draining && !_draining)
                Add(StatKind.Death);
            ExpAndLevel(last.Host, now.Host, Add);
            if (time <= _collectUntil)
                Collected(last, now, Add);
            Repairs(last, now, Add);
        }

        if (draining != _draining)
            Add(draining ? StatKind.DrainStart : StatKind.DrainEnd);
        Kills(now, Add);

        _draining = draining;
        _last = now;
        return events;
    }

    /// <summary>Бот остановлен: конец слива, если шёл, и конец сессии — временем последнего снимка. Сессии не было — пусто.</summary>
    public IReadOnlyList<StatEvent> End()
    {
        if (_last is not { } last)
            return [];

        var events = new List<StatEvent>();
        if (_draining)
            events.Add(new StatEvent(last.Time, StatKind.DrainEnd));
        events.Add(new StatEvent(last.Time, StatKind.SessionEnd));

        _last = null;
        _draining = false;
        _engaged.Clear();
        _collectUntil = DateTime.MinValue;
        _repairedAt = DateTime.MinValue;
        return events;
    }

    private void Kills(WorldState now, Action<StatKind, string, long> add)
    {
        var pet = now.Pet is { IsSummoned: true } p ? p.ActiveWid : 0u;
        var petTarget = pet == 0 ? 0u : now.Npcs.FirstOrDefault(n => n.Wid == pet)?.TargetWid ?? 0u;
        foreach (var mob in now.Mobs)
        {
            if (mob.IsDead)
            {
                if (_engaged.TryGetValue(mob.Wid, out var engaged))
                {
                    add(StatKind.Kill, engaged.Name, 1);
                    _engaged.Remove(mob.Wid);
                }
            }
            else if (mob.Wid == now.Host.TargetWid || mob.Wid == petTarget || now.AttacksUs(mob))
            {
                _engaged[mob.Wid] = (mob.Name, now.Time);
            }
        }

        foreach (var old in _engaged.Where(e => now.Time - e.Value.At > EngagedMemory).Select(e => e.Key).ToList())
            _engaged.Remove(old);
    }

    private static void ExpAndLevel(HostState was, HostState now, Action<StatKind, string, long> add)
    {
        // Уровень 0 — перс ещё не прочитан как следует; падать уровень не может
        if (was.Level <= 0 || now.Level < was.Level)
            return;

        for (var level = was.Level + 1; level <= now.Level; level++)
            add(StatKind.Level, "", level);

        if (was.Exp is not { } before || now.Exp is not { } after)
            return;

        // Новый уровень: добор до уровня (сколько не хватало) и опыт на новом. Через несколько уровней разом — не посчитать
        long gained = now.Level == was.Level ? after - before
            : now.Level == was.Level + 1 && was.ExpToLevel is { } need ? Math.Max(0, need - before) + Math.Max(0, after)
            : 0;
        if (gained > 0)
            add(StatKind.Exp, "", gained);
    }

    private static void Collected(WorldState was, WorldState now, Action<StatKind, string, long> add)
    {
        if (was.Host.Money is { } before && now.Host.Money is { } after && after > before)
            add(StatKind.Money, "", after - before);

        // Сумка не прочиталась (0 ячеек) — сравнивать не с чем
        if (was.InventorySlots <= 0 || now.InventorySlots <= 0)
            return;

        var had = was.Inventory.GroupBy(i => i.Tid).ToDictionary(g => g.Key, g => g.Sum(i => (long)i.Count));
        foreach (var item in now.Inventory.GroupBy(i => i.Tid))
        {
            var more = item.Sum(i => (long)i.Count) - (had.TryGetValue(item.Key, out var count) ? count : 0);
            if (more > 0)
                add(StatKind.Item, item.Select(i => i.Name).FirstOrDefault(n => n is not null) ?? $"предмет {item.Key}", more);
        }
    }

    private void Repairs(WorldState was, WorldState now, Action<StatKind, string, long> add)
    {
        var repaired = now.Equipment.Any(i => i.MaxDurability > 0
            && was.Equipment.FirstOrDefault(w => w.Slot == i.Slot && w.Tid == i.Tid) is { } before && i.Durability > before.Durability);
        if (!repaired)
            return;

        if (now.Time - _repairedAt > RepairMerge)
            add(StatKind.Repair, "", 1);
        _repairedAt = now.Time;
    }
}

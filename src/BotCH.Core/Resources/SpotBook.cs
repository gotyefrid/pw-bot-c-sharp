using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Logging;
using BotCH.Core.World;

namespace BotCH.Core.Resources;

/// <summary>
/// Блокнот точек ресурсов: на каждом снимке запоминает, где видны ресурсы, и замечает, что ресурс пропал (выкопали).
/// Работает в любом режиме и без «Старт» — точки копятся, пока игрок ходит сам. Общий на все серверы и персонажей:
/// ресурсы во всех версиях игры лежат на одних и тех же местах.
/// </summary>
public sealed class SpotBook(IEnumerable<ResourceSpot> spots, ILogger? log = null)
{
    /// <summary>
    /// Ресурс с тем же названием ближе — та же точка (участок), если у неё на этом сервере нет другого номера:
    /// участок ~110 м в поперечнике, от центра до ~55 м.
    /// </summary>
    public const float MergeRadius = 60f;

    /// <summary>
    /// Тот же номер ресурса — та же точка, если ближе этого: возрождается в другом месте участка, до ~110 м от прошлого;
    /// дальше — номер, видимо, достался другому месту (после перезапуска сервера номера могут быть другими).
    /// </summary>
    public const float SameIdRadius = 130f;

    /// <summary>
    /// Ближе к точке клиент видит ресурсы наверняка. Замер 2026-10-03: пропадают и появляются пачками на 90–125 м, но
    /// в работе мигали и с 79 м («выкопан» и через 0.3–1.5 мин «появился» на том же месте) — берём с большим запасом.
    /// Ресурса нет в списке ближе этого — значит, его нет.
    /// </summary>
    public const float SureVisible = 50f;

    /// <summary>
    /// Ближе к центру точки без ресурса (и мы его не копали) — «пусто»: выкопал кто-то другой или ещё не появился.
    /// Ресурс может быть в ~55 м от центра, а виден наверняка с ~80 м — отсюда видно весь участок.
    /// </summary>
    public const float EmptyCheck = 25f;
    private static readonly TimeSpan EmptyAfter = TimeSpan.FromSeconds(3);

    // Пропал на столько подряд — правда пропал, а не мигнул список (загрузка после телепорта)
    private static readonly TimeSpan GoneAfter = TimeSpan.FromSeconds(2);
    // Перс сместился за снимок дальше — телепорт или загрузка: что было видно, забываем, не считая выкопанным
    private const float JumpDistance = 30f;
    // Центр — среднее последних появлений, старые постепенно забываются
    private const int CenterWeight = 20;

    private readonly List<ResourceSpot> _spots = spots.Where(s => s.Name.Length > 0).ToList();
    private readonly ILogger _log = log ?? NullLogger.Instance;
    private readonly HashSet<ResourceSpot> _present = [];
    // Где ресурс точки виден сейчас (точка — участок, ресурс может быть далеко от центра)
    private readonly Dictionary<ResourceSpot, Position> _at = [];
    private readonly Dictionary<ResourceSpot, DateTime> _missingSince = [];
    // Удалённые за эту сессию: слияние с файлом не возвращает их обратно
    private readonly List<ResourceSpot> _removed = [];
    // Рядом с точкой без ресурса: с какого момента; и о каких «пусто» уже сказали (до следующего ухода или появления)
    private readonly Dictionary<ResourceSpot, DateTime> _emptySince = [];
    private readonly HashSet<ResourceSpot> _emptyReported = [];

    // Наша копка: пошла полоска копания у перса — запоминаем ресурс рядом и сумку. Он пропал вскоре после полоски — выкопали мы
    private const float DigReach = 8f;
    private static readonly TimeSpan DigLinger = TimeSpan.FromSeconds(5);
    private Dig? _dig;
    private bool _wasDigging;

    private sealed class Dig(uint itemId, DateTime start, Dictionary<uint, int> bagBefore)
    {
        public uint ItemId { get; } = itemId;
        public DateTime Start { get; } = start;
        public Dictionary<uint, int> BagBefore { get; } = bagBefore;
        public DateTime LastActive { get; set; } = start;
    }

    /// <summary>Что произошло с точками — для журнала наблюдений (resource-events.csv).</summary>
    public event Action<SpotEvent>? Happened;
    private Position? _lastHost;

    public IReadOnlyList<ResourceSpot> Spots => _spots;

    /// <summary>Сервер подключённого клиента (id профиля): номера ресурсов у каждого сервера свои.</summary>
    public string Server { get; set; } = "";

    /// <summary>Есть изменения, которые ещё не сохранены в файл.</summary>
    public bool Changed { get; private set; }

    /// <summary>Ресурс этой точки сейчас виден.</summary>
    public bool IsPresent(ResourceSpot spot) => _present.Contains(spot);

    public void MarkSaved() => Changed = false;

    /// <summary>Снимок мира: новые точки, уточнение старых, кто пропал.</summary>
    public void Observe(WorldState w)
    {
        var host = w.Host.Position;
        if (!host.IsFinite)
            return;
        if (_lastHost is { } last && last.HorizontalDistanceTo(host) > JumpDistance)
            Forget();
        _lastHost = host;

        var now = w.Time;
        TrackDigging(w);
        var seen = new HashSet<ResourceSpot>();
        // «Нересурсы» (квестовые, особые) появляются на время — в точки не записываем
        foreach (var item in w.GroundItems.Where(i => i.Kind == GroundItemKind.Resource && !i.Special && i.Name.Trim().Length > 0 && i.Position.IsFinite))
        {
            var name = item.Name.Trim();
            var spot = ById(name, item.Id, item.Position, seen) ?? Nearest(name, item.Position, MergeRadius, exclude: seen, id: item.Id)
                ?? AddSeen(item, host, now);
            seen.Add(spot);
            if (Server.Length > 0 && item.Id != 0 && (!spot.Ids.TryGetValue(Server, out var id) || id != item.Id))
                (spot.Ids[Server], Changed) = (item.Id, true);
            if (!_present.Contains(spot))
                Appeared(spot, item, host, now);
            _at[spot] = item.Position;
            spot.LastSeen = now;
        }

        foreach (var spot in _present.Where(s => !seen.Contains(s)).ToList())
        {
            // Ушли дальше — просто не видно. Ближе SureVisible — исчез у нас на глазах
            var at = _at.TryGetValue(spot, out var where) ? where : spot.Position;
            if (host.HorizontalDistanceTo(at) > SureVisible)
            {
                _present.Remove(spot);
                _missingSince.Remove(spot);
                _at.Remove(spot);
                continue;
            }

            if (!_missingSince.TryGetValue(spot, out var since))
            {
                _missingSince[spot] = now;
                continue;
            }
            if (now - since < GoneAfter)
                continue;

            _present.Remove(spot);
            _missingSince.Remove(spot);
            _at.Remove(spot);
            spot.Gone[Server] = since;
            Changed = true;
            var (who, gained, digSeconds) = WhoDug(w, spot, since);
            var loot = string.Join(", ", gained.Select(g => $"{g.Key}×{g.Value}"));
            _log.Info(who == "мы"
                ? $"{spot.Name}: выкопали мы (копка {digSeconds:0.0} с), в сумку {(loot.Length > 0 ? loot : "ничего не прибавилось")}"
                : $"{spot.Name} пропал, {host.HorizontalDistanceTo(at):0} м — " + (who == "другой" ? "выкопал кто-то другой" : "кто — не видно"));
            Tell(SpotEventKind.Dug, since, spot, at, IdHere(spot), host, who: who, gained: gained, digSeconds: digSeconds);
        }

        foreach (var spot in seen)
            _missingSince.Remove(spot);

        CheckEmpty(host, now);
    }

    private void TrackDigging(WorldState w)
    {
        var digging = w.Host.Gather is { Active: true };
        if (digging && !_wasDigging)
        {
            var host = w.Host.Position;
            var item = w.GroundItems
                .Where(i => i.Kind == GroundItemKind.Resource && !i.Special && i.Position.HorizontalDistanceTo(host) <= DigReach)
                .OrderBy(i => i.Position.HorizontalDistanceTo(host))
                .FirstOrDefault();
            _dig = item is null ? null : new Dig(item.Id, w.Time, Bag(w));
        }
        if (digging && _dig is not null)
            _dig.LastActive = w.Time;
        _wasDigging = digging;
    }

    // Кто выкопал: мы — шла наша полоска у этого ресурса и кончилась незадолго до пропажи; что прибавилось в сумке
    private (string Who, Dictionary<uint, int> Gained, double? DigSeconds) WhoDug(WorldState w, ResourceSpot spot, DateTime gone)
    {
        if (w.Host.Gather is null)
            return ("", [], null);
        if (_dig is not { } dig || dig.ItemId != IdHere(spot) || gone - dig.LastActive > DigLinger || gone < dig.Start)
            return ("другой", [], null);

        _dig = null;
        var gained = Bag(w)
            .Select(b => (Tid: b.Key, Gain: b.Value - (dig.BagBefore.TryGetValue(b.Key, out var was) ? was : 0)))
            .Where(b => b.Gain > 0)
            .ToDictionary(b => b.Tid, b => b.Gain);
        return ("мы", gained, (dig.LastActive - dig.Start).TotalSeconds);
    }

    private static Dictionary<uint, int> Bag(WorldState w)
        => w.Inventory.GroupBy(i => i.Tid).ToDictionary(g => g.Key, g => g.Sum(i => i.Count));

    // Подошли к известной точке, ресурса нет, и мы не видели, как его копали — «пусто» (раз за подход)
    private void CheckEmpty(Position host, DateTime now)
    {
        foreach (var spot in _spots)
        {
            var distance = host.HorizontalDistanceTo(spot.Position);
            if (_present.Contains(spot) || distance > SureVisible)
            {
                _emptySince.Remove(spot);
                _emptyReported.Remove(spot);
                continue;
            }
            if (distance > EmptyCheck || spot.GoneOn(Server) is not null || _emptyReported.Contains(spot))
                continue;

            if (!_emptySince.TryGetValue(spot, out var since))
                _emptySince[spot] = since = now;
            if (now - since < EmptyAfter)
                continue;

            _emptyReported.Add(spot);
            _emptySince.Remove(spot);
            Tell(SpotEventKind.Empty, now, spot, spot.Position, IdHere(spot), host);
        }
    }

    private uint IdHere(ResourceSpot spot) => Server.Length > 0 && spot.Ids.TryGetValue(Server, out var id) ? id : 0;

    private void Tell(SpotEventKind kind, DateTime time, ResourceSpot spot, Position at, uint id, Position host, float fromCenter = 0,
        TimeSpan? sinceDug = null, string who = "", Dictionary<uint, int>? gained = null, double? digSeconds = null)
        => Happened?.Invoke(new SpotEvent(time, kind, spot.Name, id, at, spot.Position, fromCenter, sinceDug, host.HorizontalDistanceTo(at))
            { Who = who, Gained = gained ?? [], DigSeconds = digSeconds });

    /// <summary>Персонаж не в мире (загрузка, выход): что было видно, забываем, не считая выкопанным.</summary>
    public void Forget()
    {
        _present.Clear();
        _at.Clear();
        _missingSince.Clear();
        _emptySince.Clear();
        _emptyReported.Clear();
        _lastHost = null;
    }

    /// <summary>Точка вручную: персонаж стоит у места, где ресурс бывает (сейчас его может не быть).</summary>
    public ResourceSpot Add(string name, Position position)
    {
        name = name.Trim();
        if (name.Length == 0)
            throw new ArgumentException("Нет названия", nameof(name));

        if (Nearest(name, position, MergeRadius) is { } known)
        {
            _log.Info($"{name}: точка уже есть, {known.Position.HorizontalDistanceTo(position):0} м отсюда");
            return known;
        }

        var spot = new ResourceSpot { Name = name, Position = position, Manual = true };
        _spots.Add(spot);
        Changed = true;
        _log.Info($"Точка «{name}» добавлена вручную, всего точек {_spots.Count}");
        return spot;
    }

    public bool Remove(ResourceSpot spot)
    {
        if (!_spots.Remove(spot))
            return false;

        _present.Remove(spot);
        _at.Remove(spot);
        _missingSince.Remove(spot);
        _removed.Add(spot);
        Changed = true;
        _log.Info($"Точка «{spot.Name}» удалена, осталось {_spots.Count}");
        return true;
    }

    /// <summary>
    /// Слить с точками из общего файла (их пишут и другие копии бота): незнакомые добавляются, у знакомых — самое
    /// свежее «видели»/«выкопан» и номера с других серверов. Возвращает, сколько точек добавилось.
    /// </summary>
    public int Merge(IEnumerable<ResourceSpot> other)
    {
        var added = 0;
        foreach (var o in other.Where(o => o.Name.Trim().Length > 0))
        {
            if (_removed.Any(r => Same(r, o)))
                continue;

            var spot = _spots.FirstOrDefault(s => Same(s, o));
            if (spot is null)
            {
                _spots.Add(o);
                added++;
                continue;
            }

            if (o.LastSeen > spot.LastSeen)
                spot.LastSeen = o.LastSeen;
            // Копки — по серверам, самая свежая; ресурс нашего сервера сейчас виден — он не выкопан
            foreach (var gone in o.Gone)
            {
                if (gone.Key == Server && _present.Contains(spot))
                    continue;
                if (spot.GoneOn(gone.Key) is not { } known || gone.Value > known)
                    spot.Gone[gone.Key] = gone.Value;
            }
            spot.Seen = Math.Max(spot.Seen, o.Seen);
            spot.Spread = Math.Max(spot.Spread, o.Spread);
            foreach (var id in o.Ids.Where(id => !spot.Ids.ContainsKey(id.Key)))
                spot.Ids[id.Key] = id.Value;
        }

        if (added > 0)
            _log.Debug($"Из общего файла добавлено точек: {added}, всего {_spots.Count}");
        Consolidate();
        return added;
    }

    /// <summary>
    /// Склеить точки одного участка: то же название и тот же номер на каком-то сервере (и нигде не разные номера).
    /// Раньше точка считалась местом (сливались только ближе 20 м), и возродившийся в другом углу участка ресурс
    /// заводил новую точку.
    /// </summary>
    public void Consolidate()
    {
        var merged = 0;
        for (var i = 0; i < _spots.Count; i++)
        {
            for (var j = _spots.Count - 1; j > i; j--)
            {
                var (a, b) = (_spots[i], _spots[j]);
                if (!a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase) || a.Position.HorizontalDistanceTo(b.Position) > SameIdRadius
                    || !a.Ids.Any(id => b.Ids.TryGetValue(id.Key, out var other) && other == id.Value)
                    || a.Ids.Any(id => b.Ids.TryGetValue(id.Key, out var other) && other != id.Value))
                    continue;

                Fold(a, b);
                _spots.RemoveAt(j);
                if (_present.Remove(b))
                    _present.Add(a);
                if (_at.TryGetValue(b, out var at))
                {
                    _at[a] = at;
                    _at.Remove(b);
                }
                _missingSince.Remove(b);
                merged++;
            }
        }

        if (merged == 0)
            return;
        Changed = true;
        _log.Info($"Склеено точек одного участка: {merged}, всего точек {_spots.Count}");
    }

    // b — та же точка, что a: центр — среднее по числу появлений, остальное — самое полное
    private static void Fold(ResourceSpot a, ResourceSpot b)
    {
        var (wa, wb) = (Math.Max(a.Seen, 1), Math.Max(b.Seen, 1));
        var center = new Position(
            (a.X * wa + b.X * wb) / (wa + wb), (a.Height * wa + b.Height * wb) / (wa + wb), (a.Y * wa + b.Y * wb) / (wa + wb));
        a.Spread = Math.Max(a.Spread + a.Position.HorizontalDistanceTo(center), b.Spread + b.Position.HorizontalDistanceTo(center));
        a.Position = center;
        a.Seen += b.Seen;
        if (a.LastSeen is null || b.LastSeen > a.LastSeen)
            a.LastSeen = b.LastSeen;
        foreach (var gone in b.Gone)
        {
            if (a.GoneOn(gone.Key) is not { } known || gone.Value > known)
                a.Gone[gone.Key] = gone.Value;
        }
        foreach (var id in b.Ids.Where(id => !a.Ids.ContainsKey(id.Key)))
            a.Ids[id.Key] = id.Value;
        a.Manual |= b.Manual;
    }

    // Одна и та же точка в двух списках: тот же номер на каком-то сервере, иначе то же название ближе MergeRadius
    private static bool Same(ResourceSpot a, ResourceSpot b)
    {
        if (!a.Name.Equals(b.Name, StringComparison.OrdinalIgnoreCase))
            return false;
        var distance = a.Position.HorizontalDistanceTo(b.Position);
        return distance <= MergeRadius
            || (distance <= SameIdRadius && a.Ids.Any(id => b.Ids.TryGetValue(id.Key, out var other) && other == id.Value));
    }

    // id — номер увиденного ресурса: точка, у которой на этом сервере другой номер, — соседний участок, не эта
    private ResourceSpot? Nearest(string name, Position position, float radius, ISet<ResourceSpot>? exclude = null, uint id = 0)
        => _spots
            .Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && exclude?.Contains(s) != true
                        && (id == 0 || Server.Length == 0 || !s.Ids.TryGetValue(Server, out var known) || known == id))
            .Select(s => (Spot: s, Distance: s.Position.HorizontalDistanceTo(position)))
            .Where(x => x.Distance <= radius)
            .OrderBy(x => x.Distance)
            .Select(x => x.Spot)
            .FirstOrDefault();

    private ResourceSpot? ById(string name, uint id, Position position, ISet<ResourceSpot> exclude)
        => id == 0 || Server.Length == 0 ? null : _spots.FirstOrDefault(s => s.Ids.TryGetValue(Server, out var known) && known == id && !exclude.Contains(s)
            && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Position.HorizontalDistanceTo(position) <= SameIdRadius);

    private ResourceSpot AddSeen(GroundItem item, Position host, DateTime now)
    {
        var spot = new ResourceSpot { Name = item.Name.Trim(), Position = item.Position };
        _spots.Add(spot);
        _log.Info($"Новая точка: {spot.Name} {spot.Position}, всего точек {_spots.Count}");
        Tell(SpotEventKind.New, now, spot, item.Position, item.Id, host);
        return spot;
    }

    // Ресурс появился в поле зрения: центр сдвигается к новому месту, разброс — самое дальнее появление
    private void Appeared(ResourceSpot spot, GroundItem item, Position host, DateTime now)
    {
        var at = item.Position;
        var fromCenter = spot.Position.HorizontalDistanceTo(at);
        if (spot.Seen > 0)
        {
            // Видели, как выкопали, — это новое появление; иначе точка просто снова попала в поле зрения
            if (spot.GoneOn(Server) is { } gone)
            {
                var after = now - gone;
                _log.Info($"{spot.Name} появился снова через {after.TotalMinutes:0.0} мин после копки, в {fromCenter:0} м от центра точки");
                Tell(SpotEventKind.Respawned, now, spot, at, item.Id, host, fromCenter, after);
            }
            else
            {
                Tell(SpotEventKind.InView, now, spot, at, item.Id, host, fromCenter);
            }
        }

        _present.Add(spot);
        _emptySince.Remove(spot);
        _emptyReported.Remove(spot);
        spot.Gone.Remove(Server);
        if (spot.Seen == 0)
        {
            // Ручная точка — туда, где ресурс правда появился
            spot.Position = at;
        }
        else
        {
            var c = spot.Position;
            var k = 1f / (Math.Min(spot.Seen, CenterWeight - 1) + 1);
            spot.Position = new Position(c.X + (at.X - c.X) * k, c.Height + (at.Height - c.Height) * k, c.Y + (at.Y - c.Y) * k);
            spot.Spread = Math.Max(spot.Spread, spot.Position.HorizontalDistanceTo(at));
        }

        spot.Seen++;
        Changed = true;
    }
}

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
    /// <summary>Ресурс с тем же названием ближе — та же точка (появился снова с разбросом).</summary>
    public const float MergeRadius = 20f;

    /// <summary>
    /// Тот же номер ресурса — та же точка, если ближе этого: разброс бывает и больше <see cref="MergeRadius"/>,
    /// а дальше — номер, видимо, достался другому месту (после перезапуска сервера номера могут быть другими).
    /// </summary>
    public const float SameIdRadius = 50f;

    /// <summary>
    /// Ближе к точке клиент видит ресурсы наверняка: замер 2026-10-03 — пропадают и появляются на 90–125 м, пачками.
    /// Ресурса нет в списке ближе этого — значит, его нет.
    /// </summary>
    public const float SureVisible = 90f;

    /// <summary>Ближе к точке без ресурса (и мы его не копали) — «пусто»: выкопал кто-то другой или ещё не появился.</summary>
    public const float EmptyCheck = 80f;
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
    private readonly Dictionary<ResourceSpot, DateTime> _missingSince = [];
    // Удалённые за эту сессию: слияние с файлом не возвращает их обратно
    private readonly List<ResourceSpot> _removed = [];
    // Рядом с точкой без ресурса: с какого момента; и о каких «пусто» уже сказали (до следующего ухода или появления)
    private readonly Dictionary<ResourceSpot, DateTime> _emptySince = [];
    private readonly HashSet<ResourceSpot> _emptyReported = [];

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
        var seen = new HashSet<ResourceSpot>();
        foreach (var item in w.GroundItems.Where(i => i.Kind == GroundItemKind.Resource && i.Name.Trim().Length > 0 && i.Position.IsFinite))
        {
            var name = item.Name.Trim();
            var spot = ById(name, item.Id, item.Position, seen) ?? Nearest(name, item.Position, MergeRadius, exclude: seen) ?? AddSeen(item, host, now);
            seen.Add(spot);
            if (Server.Length > 0 && item.Id != 0 && (!spot.Ids.TryGetValue(Server, out var id) || id != item.Id))
                (spot.Ids[Server], Changed) = (item.Id, true);
            if (!_present.Contains(spot))
                Appeared(spot, item, host, now);
            spot.LastSeen = now;
        }

        foreach (var spot in _present.Where(s => !seen.Contains(s)).ToList())
        {
            // Ушли дальше — просто не видно. Ближе SureVisible — исчез у нас на глазах
            if (host.HorizontalDistanceTo(spot.Position) > SureVisible)
            {
                _present.Remove(spot);
                _missingSince.Remove(spot);
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
            spot.GoneAt = since;
            Changed = true;
            _log.Info($"{spot.Name} пропал (выкопали), {host.HorizontalDistanceTo(spot.Position):0} м");
            Tell(SpotEventKind.Dug, since, spot, spot.Position, IdHere(spot), host);
        }

        foreach (var spot in seen)
            _missingSince.Remove(spot);

        CheckEmpty(host, now);
    }

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
            if (distance > EmptyCheck || spot.GoneAt is not null || _emptyReported.Contains(spot))
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

    private void Tell(SpotEventKind kind, DateTime time, ResourceSpot spot, Position at, uint id, Position host, float fromCenter = 0, TimeSpan? sinceDug = null)
        => Happened?.Invoke(new SpotEvent(time, kind, spot.Name, id, at, spot.Position, fromCenter, sinceDug, host.HorizontalDistanceTo(at)));

    /// <summary>Персонаж не в мире (загрузка, выход): что было видно, забываем, не считая выкопанным.</summary>
    public void Forget()
    {
        _present.Clear();
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
            if (o.GoneAt is { } gone && !_present.Contains(spot) && gone > (spot.GoneAt ?? DateTime.MinValue) && gone > (spot.LastSeen ?? DateTime.MinValue))
                spot.GoneAt = gone;
            spot.Seen = Math.Max(spot.Seen, o.Seen);
            spot.Spread = Math.Max(spot.Spread, o.Spread);
            foreach (var id in o.Ids.Where(id => !spot.Ids.ContainsKey(id.Key)))
                spot.Ids[id.Key] = id.Value;
        }

        if (added > 0)
            _log.Debug($"Из общего файла добавлено точек: {added}, всего {_spots.Count}");
        return added;
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

    private ResourceSpot? Nearest(string name, Position position, float radius, ISet<ResourceSpot>? exclude = null)
        => _spots
            .Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && exclude?.Contains(s) != true)
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
            if (spot.GoneAt is { } gone)
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
        spot.GoneAt = null;
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

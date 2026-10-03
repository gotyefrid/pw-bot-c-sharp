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
    private Position? _lastHost;

    public IReadOnlyList<ResourceSpot> Spots => _spots;

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
            var spot = ById(name, item.Id, item.Position, seen) ?? Nearest(name, item.Position, MergeRadius, exclude: seen) ?? AddSeen(item);
            seen.Add(spot);
            if (spot.ResourceId != item.Id)
                (spot.ResourceId, Changed) = (item.Id, true);
            if (!_present.Contains(spot))
                Appeared(spot, item.Position);
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
        }

        foreach (var spot in seen)
            _missingSince.Remove(spot);
    }

    /// <summary>Персонаж не в мире (загрузка, выход): что было видно, забываем, не считая выкопанным.</summary>
    public void Forget()
    {
        _present.Clear();
        _missingSince.Clear();
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
        Changed = true;
        _log.Info($"Точка «{spot.Name}» удалена, осталось {_spots.Count}");
        return true;
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
        => id == 0 ? null : _spots.FirstOrDefault(s => s.ResourceId == id && !exclude.Contains(s)
            && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Position.HorizontalDistanceTo(position) <= SameIdRadius);

    private ResourceSpot AddSeen(GroundItem item)
    {
        var spot = new ResourceSpot { Name = item.Name.Trim(), Position = item.Position };
        _spots.Add(spot);
        _log.Info($"Новая точка: {spot.Name} {spot.Position}, всего точек {_spots.Count}");
        return spot;
    }

    // Ресурс появился в поле зрения: центр сдвигается к новому месту, разброс — самое дальнее появление
    private void Appeared(ResourceSpot spot, Position at)
    {
        _present.Add(spot);
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

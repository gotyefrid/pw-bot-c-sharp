using System;
using System.Collections.Generic;
using System.Text;
using Notes.Core.Memory;
using Notes.Core.Profiles;

namespace Notes.Core.World;

/// <summary>Точка из списка мастера телепортации: <see cref="Index"/> — номер строки в его списке (его и просим у NPC).</summary>
public sealed record TeleportPoint(int Index, int Id, string? Name, Position Position, int Fee);

/// <summary>
/// Куда ведёт мастер телепортации: по записи NPC в справочнике — номер его услуги, по нему — список точек, названия и
/// координаты — из таблицы точек игры. Справочник не меняется, пока клиент запущен: у каждой записи NPC читаем один раз.
/// </summary>
internal sealed class TeleportReader(IMemory memory, GameRoots roots, ProfileData profile)
{
    // Больше точек у мастера клиент не берёт; точек в игре — 123, с запасом
    private const int MaxDestinations = 32;
    private const uint MaxWaypoints = 1024;
    private const int MaxChain = 64;

    private readonly Dictionary<uint, IReadOnlyList<TeleportPoint>?> _byNpc = [];
    private Dictionary<int, (string? Name, Position Position)>? _waypoints;
    private IReadOnlyList<TeleportPoint>? _all;

    /// <summary>Все точки телепорта игры (<see cref="TeleportPoint.Index"/> — 0, у мастера не нужен); пусто — не найдены.</summary>
    public IReadOnlyList<TeleportPoint> All()
    {
        if (_all is { } known)
            return known;
        try
        {
            var list = new List<TeleportPoint>();
            foreach (var p in Waypoints())
                list.Add(new TeleportPoint(0, p.Key, p.Value.Name, p.Value.Position, 0));
            return list.Count == 0 ? list : _all = list;
        }
        catch (MemoryAccessException)
        {
            return [];
        }
    }

    /// <summary>Список точек мастера по записи NPC (<see cref="NpcOffsets.Essence"/>); null — не мастер или не читается.</summary>
    public IReadOnlyList<TeleportPoint>? Destinations(uint npcRecord)
    {
        var t = profile.Teleports;
        if (npcRecord == 0 || t.NpcService == 0 || profile.Packets.TransmitService == 0)
            return null;
        if (_byNpc.TryGetValue(npcRecord, out var known))
            return known;

        try
        {
            if (!memory.TryReadUInt32(npcRecord + t.NpcService, out var service))
                return null;
            return _byNpc[npcRecord] = service == 0 ? null : ReadList(service);
        }
        catch (MemoryAccessException)
        {
            return null;
        }
    }

    private IReadOnlyList<TeleportPoint>? ReadList(uint service)
    {
        var t = profile.Teleports;
        if (Record(service, out var kind) is not { } record || kind != t.ServiceKind)
            return null;
        var count = Math.Min(memory.ReadInt32(record + t.DestinationCount), MaxDestinations);
        var waypoints = Waypoints();
        var list = new List<TeleportPoint>();
        for (var i = 0; i < count; i++)
        {
            var row = record + t.Destinations + (uint)i * t.DestinationSize;
            var id = memory.ReadInt32(row);
            if (id == 0 || !waypoints.TryGetValue(id, out var point))
                continue;
            list.Add(new TeleportPoint(i, id, point.Name, point.Position, memory.ReadInt32(row + t.DestinationFee)));
        }
        return list;
    }

    // Запись справочника по её номеру; null — нет такой
    private uint? Record(uint key, out uint kind)
    {
        var e = profile.ElementData;
        kind = 0;
        if (e.Manager == 0)
            return null;
        var manager = memory.ReadUInt32(roots.Base() + e.Manager);
        var table = manager + e.Table;
        var buckets = memory.ReadUInt32(table + e.BucketCount);
        if (manager == 0 || buckets == 0)
            return null;
        var node = memory.ReadUInt32(memory.ReadUInt32(table + e.Buckets) + key % buckets * 4);
        for (var i = 0; node != 0 && i < MaxChain; i++, node = memory.ReadUInt32(node))
        {
            if (memory.ReadUInt32(node + e.NodeKey) != key)
                continue;
            kind = memory.ReadUInt32(node + e.NodeKind);
            var slot = memory.ReadUInt32(node + e.NodeSlot);
            var records = memory.ReadUInt32(memory.ReadUInt32(manager + e.Records) + kind * 4);
            var size = memory.ReadUInt32(memory.ReadUInt32(manager + e.RecordSizes) + kind * 4);
            return records + slot * size;
        }
        return null;
    }

    // Таблица точек игры: номер → название и координаты (не меняется — читаем один раз)
    private Dictionary<int, (string? Name, Position Position)> Waypoints()
    {
        if (_waypoints is { } known)
            return known;
        var t = profile.Teleports;
        var result = new Dictionary<int, (string?, Position)>();
        if (roots.Waypoints() is { } at && t.WaypointSize != 0)
        {
            var array = memory.ReadUInt32(at.Table);
            var count = Math.Min(memory.ReadUInt32(at.Count), MaxWaypoints);
            if (array != 0 && count > 0)
            {
                var block = MemoryBlock.Read(memory, array, (int)(count * t.WaypointSize));
                for (uint i = 0; i < count; i++)
                {
                    var o = i * t.WaypointSize;
                    var id = block.Int32(o);
                    var name = Text(block, o + t.WaypointName, t.WaypointPosition - t.WaypointName - 4);
                    var p = o + t.WaypointPosition;
                    result[id] = (name, new Position(block.Float(p), block.Float(p + 4), block.Float(p + 8)));
                }
            }
        }
        // Пусто (игра ещё грузится) — прочтём в другой раз
        return result.Count == 0 ? result : _waypoints = result;
    }

    // Строка UTF-16 внутри записи, не длиннее поля
    private static string? Text(MemoryBlock block, uint offset, uint maxBytes)
    {
        var chars = new StringBuilder();
        for (uint i = 0; i + 1 < maxBytes; i += 2)
        {
            var c = (char)block.UInt16(offset + i);
            if (c == '\0')
                break;
            chars.Append(c);
        }
        return chars.Length == 0 ? null : chars.ToString();
    }
}

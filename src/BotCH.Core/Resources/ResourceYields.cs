using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.World;

namespace BotCH.Core.Resources;

/// <summary>
/// Что даёт ресурс при копке: по серверу и названию — какие предметы (tid) и сколько самое большее за раз.
/// Учится на наших копках (что прибавилось в сумке); нужно, чтобы при полной сумке копать то, что ляжет в начатые стопки.
/// Номера предметов у серверов свои — поэтому по серверу. Читается из потока мозга, пополняется из окна — под замком.
/// </summary>
public sealed class ResourceYields
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Dictionary<string, Dictionary<uint, int>>> _servers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Есть изменения, которые ещё не сохранены в файл.</summary>
    public bool Changed { get; private set; }

    public void MarkSaved() => Changed = false;

    /// <summary>Наша копка дала <paramref name="gained"/>. Возвращает true, если узнали что-то новое.</summary>
    public bool Learn(string server, string name, IReadOnlyDictionary<uint, int> gained)
    {
        name = name.Trim();
        if (server.Length == 0 || name.Length == 0)
            return false;

        lock (_lock)
        {
            var learned = false;
            foreach (var g in gained.Where(g => g.Value > 0))
                learned |= Put(server, name, g.Key, g.Value);
            Changed |= learned;
            return learned;
        }
    }

    /// <summary>Что даёт ресурс: tid → самое большее за копку. Пусто — ещё не копали на этом сервере.</summary>
    public IReadOnlyDictionary<uint, int> Of(string server, string name)
    {
        lock (_lock)
        {
            return _servers.TryGetValue(server, out var names) && names.TryGetValue(name.Trim(), out var items)
                ? new Dictionary<uint, int>(items)
                : new Dictionary<uint, int>();
        }
    }

    /// <summary>
    /// Влезет ли добыча при полной сумке: для каждого предмета, который даёт ресурс, в начатых стопках есть место
    /// на самую большую копку. Что даёт ресурс, не знаем — нет.
    /// </summary>
    public bool FitsInStacks(string server, string name, IReadOnlyList<InventoryItem> inventory)
    {
        var yields = Of(server, name);
        return yields.Count > 0 && yields.All(y =>
            inventory.Where(i => i.Tid == y.Key && i.MaxCount > 0).Sum(i => Math.Max(0, i.MaxCount - i.Count)) >= y.Value);
    }

    /// <summary>Слить с данными из общего файла (их пишут и другие копии бота): объединение, у совпавших — больший улов.</summary>
    public void Merge(IReadOnlyDictionary<string, Dictionary<string, Dictionary<uint, int>>> other)
    {
        lock (_lock)
        {
            foreach (var server in other)
            foreach (var name in server.Value)
            foreach (var item in name.Value.Where(i => i.Value > 0))
                Put(server.Key, name.Key.Trim(), item.Key, item.Value);
        }
    }

    /// <summary>Копия всех данных — для записи в файл.</summary>
    public Dictionary<string, Dictionary<string, Dictionary<uint, int>>> Snapshot()
    {
        lock (_lock)
        {
            return _servers.ToDictionary(
                s => s.Key,
                s => s.Value.ToDictionary(n => n.Key, n => new Dictionary<uint, int>(n.Value)));
        }
    }

    private bool Put(string server, string name, uint tid, int count)
    {
        if (!_servers.TryGetValue(server, out var names))
            _servers[server] = names = new Dictionary<string, Dictionary<uint, int>>(StringComparer.OrdinalIgnoreCase);
        if (!names.TryGetValue(name, out var items))
            names[name] = items = [];
        if (items.TryGetValue(tid, out var known) && known >= count)
            return false;

        items[tid] = count;
        return true;
    }
}

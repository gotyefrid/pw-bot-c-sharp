using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BotCH.Core.Resources;

/// <summary>
/// Файл точек ресурсов рядом с exe (<c>resources.json</c>) — один на все серверы и персонажей.
/// Сохранение атомарное, как у настроек (<see cref="JsonFile"/>).
/// </summary>
public sealed class SpotBookStore(string path)
{
    private static readonly JsonSerializerSettings Options = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
        // Время — всемирное: пишется с «Z», старые записи с поясом («+03:00») при чтении переводятся во всемирное
        DateTimeZoneHandling = DateTimeZoneHandling.Utc,
    };

    public string Path { get; } = path;

    /// <summary>Точки из файла. Файла нет — пусто. Испорчен — копия <c>.bad</c>, пусто, причина в <paramref name="problem"/>.</summary>
    public IReadOnlyList<ResourceSpot> Load(out string? problem)
    {
        var spots = JsonFile.Load(Path, Parse, () => [], out var details);
        problem = details is null ? null : $"Файл точек ресурсов испорчен, начинаю с пустого. {details}";
        return spots;
    }

    public void Save(IEnumerable<ResourceSpot> spots)
        => JsonFile.Save(Path, JsonConvert.SerializeObject(new SpotFile { Spots = spots.ToList() }, Options));

    private static IReadOnlyList<ResourceSpot> Parse(string json)
        => JsonConvert.DeserializeObject<SpotFile>(json, Options)?.Spots
               .Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name)).ToList() ?? [];

    private sealed class SpotFile
    {
        public int Version { get; set; } = 1;
        public List<ResourceSpot> Spots { get; set; } = [];
    }
}

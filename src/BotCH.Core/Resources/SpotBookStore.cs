using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace BotCH.Core.Resources;

/// <summary>
/// Файл точек ресурсов рядом с exe (<c>resources.json</c>) — один на все серверы и персонажей.
/// Сохранение атомарное, как у настроек: временный файл и подмена.
/// </summary>
public sealed class SpotBookStore(string path)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private static readonly JsonSerializerSettings Options = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.Indented,
    };

    public string Path { get; } = path;

    /// <summary>Точки из файла. Файла нет — пусто. Испорчен — копия <c>.bad</c>, пусто, причина в <paramref name="problem"/>.</summary>
    public IReadOnlyList<ResourceSpot> Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(Path))
            return [];

        try
        {
            var file = JsonConvert.DeserializeObject<SpotFile>(File.ReadAllText(Path, Utf8), Options);
            return file?.Spots.Where(s => s is not null && !string.IsNullOrWhiteSpace(s.Name)).ToList() ?? [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            var bad = Path + ".bad";
            try
            {
                File.Copy(Path, bad, overwrite: true);
            }
            catch (IOException)
            {
                bad = "(не удалось сохранить копию)";
            }

            problem = $"Файл точек ресурсов испорчен, начинаю с пустого. Копия: {bad}. Ошибка: {e.Message}";
            return [];
        }
    }

    public void Save(IEnumerable<ResourceSpot> spots)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(new SpotFile { Spots = spots.ToList() }, Options), Utf8);

        if (File.Exists(Path))
            File.Replace(temp, Path, destinationBackupFileName: null);
        else
            File.Move(temp, Path);
    }

    private sealed class SpotFile
    {
        public int Version { get; set; } = 1;
        public List<ResourceSpot> Spots { get; set; } = [];
    }
}

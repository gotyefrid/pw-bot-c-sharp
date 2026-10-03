using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace BotCH.Core.Resources;

/// <summary>
/// Файл «что даёт ресурс» (<c>resource-yields.json</c>) рядом с точками ресурсов, общий для всех копий бота:
/// сервер → название ресурса → tid предмета → самое большее за копку. Сохранение атомарное.
/// </summary>
public sealed class ResourceYieldsStore(string path)
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // Без camelCase: ключи — названия ресурсов как в игре
    private static readonly JsonSerializerSettings Options = new()
    {
        MissingMemberHandling = MissingMemberHandling.Ignore,
        Formatting = Formatting.Indented,
    };

    public string Path { get; } = path;

    /// <summary>Данные из файла. Файла нет — пусто. Испорчен — копия <c>.bad</c>, пусто, причина в <paramref name="problem"/>.</summary>
    public Dictionary<string, Dictionary<string, Dictionary<uint, int>>> Load(out string? problem)
    {
        problem = null;
        if (!File.Exists(Path))
            return [];

        try
        {
            var file = JsonConvert.DeserializeObject<YieldsFile>(File.ReadAllText(Path, Utf8), Options);
            return file?.Servers ?? [];
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

            problem = $"Файл «что даёт ресурс» испорчен, начинаю с пустого. Копия: {bad}. Ошибка: {e.Message}";
            return [];
        }
    }

    public void Save(ResourceYields yields)
    {
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(new YieldsFile { Servers = yields.Snapshot() }, Options), Utf8);

        if (File.Exists(Path))
            File.Replace(temp, Path, destinationBackupFileName: null);
        else
            File.Move(temp, Path);
    }

    private sealed class YieldsFile
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        [JsonProperty("servers")]
        public Dictionary<string, Dictionary<string, Dictionary<uint, int>>> Servers { get; set; } = [];
    }
}

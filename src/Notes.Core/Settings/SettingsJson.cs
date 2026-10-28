using Notes.Core.Clicker;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Notes.Core.Settings;

/// <summary>
/// JSON настроек. Незнакомые поля пропускаются, отсутствующие берутся по умолчанию —
/// файл от старой или новой версии бота открывается без ошибок.
/// </summary>
public static class SettingsJson
{
    private static readonly JsonSerializerSettings Options = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        MissingMemberHandling = MissingMemberHandling.Ignore,
        // Иначе список названий из файла добавится к списку по умолчанию, а не заменит его
        ObjectCreationHandling = ObjectCreationHandling.Replace,
        Formatting = Formatting.Indented,
        // Режимы пишутся словом («ExceptListed»), а не числом — файл можно читать и править руками; действия и условия
        // кликера читаются по своему виду («kind»)
        Converters = { new StringEnumConverter(), ClickerJson.Actions, ClickerJson.Conditions },
    };

    public static BotSettings Parse(string json)
        => (JsonConvert.DeserializeObject<BotSettings>(json, Options) ?? new BotSettings()).Normalize();

    public static string Serialize(BotSettings settings) => JsonConvert.SerializeObject(settings, Options);

    /// <summary>Часть настроек в своём файле (сценарий кликера) — по тем же правилам, что весь файл.</summary>
    public static string SerializePart(SettingsPart part) => JsonConvert.SerializeObject(part, Options);

    /// <summary>Часть настроек из своего файла — без <see cref="BotSettings.Normalize"/>: значения в пределы приводит тот, кто читает.</summary>
    public static T? ParsePart<T>(string json) where T : SettingsPart => JsonConvert.DeserializeObject<T>(json, Options);
}

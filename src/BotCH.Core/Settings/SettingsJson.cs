using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BotCH.Core.Settings;

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
        // Режимы пишутся словом («ExceptListed»), а не числом — файл можно читать и править руками
        Converters = { new StringEnumConverter() },
    };

    public static BotSettings Parse(string json)
        => (JsonConvert.DeserializeObject<BotSettings>(json, Options) ?? new BotSettings()).Normalize();

    public static string Serialize(BotSettings settings) => JsonConvert.SerializeObject(settings, Options);
}

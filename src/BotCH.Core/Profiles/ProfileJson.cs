using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace BotCH.Core.Profiles;

/// <summary>Чтение профиля из JSON с комментариями: смещения строками "0x...", сигнатуры строками "56 6A ?? E8".</summary>
public static class ProfileJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver
        {
            // ключи словаря функций — как есть
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },
        Converters = { new HexUIntConverter(), new SignatureConverter(), new StringEnumConverter(new CamelCaseNamingStrategy()) },
        // Опечатка в имени поля — ошибка, а не тихий ноль
        MissingMemberHandling = MissingMemberHandling.Error,
    };

    public static ProfileData Parse(string json)
    {
        using var reader = new JsonTextReader(new StringReader(json));
        return JsonSerializer.Create(Settings).Deserialize<ProfileData>(reader)
               ?? throw new FormatException("Пустой профиль");
    }

    public static string Serialize(ProfileData data) => JsonConvert.SerializeObject(data, Formatting.Indented, Settings);

    private sealed class HexUIntConverter : JsonConverter<uint>
    {
        public override uint ReadJson(JsonReader reader, Type objectType, uint existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                    return Convert.ToUInt32(reader.Value, CultureInfo.InvariantCulture);
                case JsonToken.String:
                    var text = ((string)reader.Value!).Trim();
                    if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        && uint.TryParse(text.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
                        return hex;
                    if (uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var dec))
                        return dec;
                    throw new JsonSerializationException($"Ожидалось число или \"0x...\", а не \"{text}\" ({reader.Path})");
                default:
                    throw new JsonSerializationException($"Ожидалось число или \"0x...\" ({reader.Path})");
            }
        }

        public override void WriteJson(JsonWriter writer, uint value, JsonSerializer serializer) => writer.WriteValue($"0x{value:X}");
    }

    private sealed class SignatureConverter : JsonConverter<Signature?>
    {
        public override Signature? ReadJson(JsonReader reader, Type objectType, Signature? existingValue, bool hasExistingValue, JsonSerializer serializer)
            => reader.TokenType == JsonToken.Null ? null : Signature.Parse((string)reader.Value!);

        public override void WriteJson(JsonWriter writer, Signature? value, JsonSerializer serializer) => writer.WriteValue(value?.ToString());
    }
}

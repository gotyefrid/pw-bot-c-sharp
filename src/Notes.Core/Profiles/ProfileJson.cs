using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Notes.Core.Profiles;

/// <summary>Чтение профиля из JSON с комментариями: смещения строками "0x...", сигнатуры и байты патча строками "56 6A ?? E8" / "74 4F".</summary>
public static class ProfileJson
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver
        {
            // ключи словаря функций — как есть
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },
        Converters = { new HexUIntConverter(), new SignatureConverter(), new HexBytesConverter(), new StringEnumConverter(new CamelCaseNamingStrategy()) },
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
                    // Пустое — на этом клиенте не проверено: как 0, поле не читается
                    if (text.Length == 0)
                        return 0;
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

    /// <summary>Байты правок патча строками «74 4F» (пробелы необязательны) — разбор при загрузке профиля, опечатка сразу видна.</summary>
    private sealed class HexBytesConverter : JsonConverter<byte[]>
    {
        public override byte[] ReadJson(JsonReader reader, Type objectType, byte[]? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String)
                throw new JsonSerializationException($"Ожидались hex-байты \"74 4F\", а не {reader.TokenType} ({reader.Path})");

            var text = ((string)reader.Value!).Trim();
            var compact = new StringBuilder(text.Length);
            foreach (var c in text)
                if (!char.IsWhiteSpace(c))
                    compact.Append(c);

            var hex = compact.ToString();
            if (hex.Length == 0 || hex.Length % 2 != 0)
                throw new JsonSerializationException($"Ожидалось чётное число hex-цифр, а не \"{text}\" ({reader.Path})");

            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                if (!Uri.IsHexDigit(hex[i * 2]) || !Uri.IsHexDigit(hex[i * 2 + 1]))
                    throw new JsonSerializationException($"\"{text}\" — не hex-байты ({reader.Path})");
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }

        public override void WriteJson(JsonWriter writer, byte[]? value, JsonSerializer serializer)
        {
            if (value is null)
            {
                writer.WriteNull();
                return;
            }

            var text = new StringBuilder(value.Length * 3);
            foreach (var b in value)
                text.Append(b.ToString("X2")).Append(' ');
            writer.WriteValue(text.ToString().TrimEnd());
        }
    }
}

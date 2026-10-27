using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BotCH.Core.Clicker;

/// <summary>
/// Действия и условия кликера в файле настроек: <c>{ "kind": "PressKey", "key": "F1" }</c>. Читается по виду
/// (<see cref="ClickerKinds"/>); вид, которого эта версия не знает, — в <see cref="UnknownAction"/> / <see cref="UnknownCondition"/>
/// с полями как были. Пишет обычный сериализатор: вид — свойство <c>Kind</c> самих классов.
/// </summary>
public static class ClickerJson
{
    public static JsonConverter Actions { get; } = new KindConverter<ClickerAction>(ClickerKinds.Actions,
        (kind, fields) => new UnknownAction(kind) { Fields = fields });

    public static JsonConverter Conditions { get; } = new KindConverter<ClickerCondition>(ClickerKinds.Conditions,
        (kind, fields) => new UnknownCondition(kind) { Fields = fields });

    private const string KindField = "kind";

    private sealed class KindConverter<T>(ClickerKindList<T> kinds, Func<string, IDictionary<string, JToken>, T> unknown) : JsonConverter
        where T : class
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type type) => type == typeof(T);

        public override object? ReadJson(JsonReader reader, Type type, object? existing, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            var json = JObject.Load(reader);
            var kind = (string?)json[KindField] ?? "";
            if (kinds.Create(kind) is not { } value)
                return unknown(kind, json.Properties().Where(p => p.Name != KindField).ToDictionary(p => p.Name, p => p.Value));

            serializer.Populate(json.CreateReader(), value);
            return value;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();
    }
}

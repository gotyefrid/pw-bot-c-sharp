using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Notes.Core.Notify;

/// <summary>Сообщение, пришедшее боту в Telegram: текст и, если это ответ, — на какое сообщение бота (его номер и текст).</summary>
public sealed record TelegramMessage(long MessageId, string Text, long? ReplyToId = null, string? ReplyToText = null);

/// <summary>Что вернул getUpdates: сообщения из нашего чата, номер последнего обновления и ошибка Telegram (код и описание).</summary>
public sealed record TelegramUpdates(IReadOnlyList<TelegramMessage> Messages, long? LastUpdateId, int? ErrorCode = null,
    string? Error = null, int Foreign = 0)
{
    /// <summary>Сообщений нет, всё прочитано.</summary>
    public static readonly TelegramUpdates Empty = new([], null);

    /// <summary>
    /// Разобрать ответ getUpdates. Сообщения не из <paramref name="chatId"/> (чужие люди, нашедшие бота) пропускаются — их
    /// только считаем (<see cref="Foreign"/>), но номер обновления всё равно засчитываем: иначе Telegram присылал бы их снова.
    /// </summary>
    public static TelegramUpdates Parse(string json, string chatId)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException)
        {
            return new([], null, null, "непонятный ответ Telegram");
        }

        if (root.Value<bool?>("ok") != true)
            return new([], null, root.Value<int?>("error_code"), root.Value<string>("description") ?? "Telegram ответил ошибкой");

        var messages = new List<TelegramMessage>();
        long? last = null;
        var foreign = 0;
        foreach (var update in root["result"] as JArray ?? new JArray())
        {
            if (update.Value<long?>("update_id") is { } id)
                last = last is { } was && was > id ? was : id;
            if (update["message"] is not JObject message || message.Value<string>("text") is not { } text)
                continue;
            if (message["chat"]?.Value<long?>("id")?.ToString(CultureInfo.InvariantCulture) != chatId.Trim())
            {
                foreign++;
                continue;
            }

            var reply = message["reply_to_message"] as JObject;
            messages.Add(new TelegramMessage(message.Value<long>("message_id"), text, reply?.Value<long?>("message_id"),
                reply?.Value<string>("text")));
        }

        return new(messages, last, Foreign: foreign);
    }
}

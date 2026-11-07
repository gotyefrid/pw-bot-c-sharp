using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;
using Notes.Core.Logging;

namespace Notes.Core.Notify;

/// <summary>
/// Сообщения в Telegram (Bot API, sendMessage). <see cref="Post"/> — в очередь: шлёт свой фоновый поток по одному, бот и поток
/// снимков не ждут сети. Не дошло — предупреждение в лог того, кто отправлял (без токена), бот работает дальше. Один на окно:
/// очередь общая у всех вкладок.
/// </summary>
public sealed class TelegramSender : IDisposable
{
    private const int TimeoutMs = 15000;

    private readonly BlockingCollection<(string Token, string ChatId, string Text, ILogger Log, bool Html, long? ReplyTo)> _queue = new();

    static TelegramSender()
    {
        // .NET Framework по умолчанию может не включать TLS 1.2, а Telegram без него не пускает
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    public TelegramSender() => new Thread(Run) { IsBackground = true, Name = "Telegram" }.Start();

    /// <summary>Отправить в фоне; не дошло — предупреждение в <paramref name="log"/>.</summary>
    /// <param name="html">Текст в разметке HTML Telegram (&lt;code&gt;, &lt;b&gt;; &lt;, &gt;, &amp; — сущностями).</param>
    /// <param name="replyTo">Ответ на это сообщение (номер в чате).</param>
    public void Post(string token, string chatId, string text, ILogger log, bool html = false, long? replyTo = null)
    {
        try
        {
            if (!_queue.IsAddingCompleted)
                _queue.Add((token, chatId, text, log, html, replyTo));
        }
        catch (InvalidOperationException)
        {
            // Окно закрывается: очередь закрыли между проверкой и добавлением
        }
    }

    /// <summary>Отправить и дождаться ответа (до 15 с). null — дошло, иначе — почему нет (без токена).</summary>
    public static string? Send(string token, string chatId, string text, bool html = false, long? replyTo = null)
    {
        try
        {
            using var web = new TimeoutWebClient { Encoding = Encoding.UTF8 };
            var values = new NameValueCollection { ["chat_id"] = chatId, ["text"] = text };
            if (html)
                values["parse_mode"] = "HTML";
            if (replyTo is { } id)
            {
                values["reply_to_message_id"] = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Сообщение, на которое отвечаем, успели удалить — всё равно отправить
                values["allow_sending_without_reply"] = "true";
            }
            web.UploadValues($"https://api.telegram.org/bot{token}/sendMessage", values);
            return null;
        }
        catch (WebException e) when (e.Response is HttpWebResponse response)
        {
            // Telegram объясняет сам: «Unauthorized» — не тот токен, «chat not found» — не тот chat_id или боту ещё не писали
            using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8);
            var description = Description(reader.ReadToEnd());
            return description is null ? $"Telegram ответил {(int)response.StatusCode}" : $"Telegram: {description}";
        }
        catch (WebException e)
        {
            return $"нет связи с Telegram ({e.Status})";
        }
    }

    private static string? Description(string json)
    {
        try
        {
            return (string?)JObject.Parse(json)["description"];
        }
        catch (Newtonsoft.Json.JsonException)
        {
            return null;
        }
    }

    private void Run()
    {
        foreach (var (token, chatId, text, log, html, replyTo) in _queue.GetConsumingEnumerable())
        {
            if (Send(token, chatId, text, html, replyTo) is { } problem)
                log.Warning($"Уведомление в Telegram не отправлено: {problem}");
        }
    }

    public void Dispose() => _queue.CompleteAdding();

    private sealed class TimeoutWebClient : WebClient
    {
        protected override WebRequest GetWebRequest(Uri address)
        {
            var request = base.GetWebRequest(address)!;
            request.Timeout = TimeoutMs;
            return request;
        }
    }
}

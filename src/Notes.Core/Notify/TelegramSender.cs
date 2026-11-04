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
/// снимков не ждут сети. Не дошло — предупреждение в лог (без токена), бот работает дальше.
/// </summary>
public sealed class TelegramSender : IDisposable
{
    private const int TimeoutMs = 15000;

    private readonly BlockingCollection<(string Token, string ChatId, string Text)> _queue = new();
    private readonly ILogger _log;

    static TelegramSender()
    {
        // .NET Framework по умолчанию может не включать TLS 1.2, а Telegram без него не пускает
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }

    public TelegramSender(ILogger log)
    {
        _log = log;
        new Thread(Run) { IsBackground = true, Name = "Telegram" }.Start();
    }

    /// <summary>Отправить в фоне.</summary>
    public void Post(string token, string chatId, string text)
    {
        if (!_queue.IsAddingCompleted)
            _queue.Add((token, chatId, text));
    }

    /// <summary>Отправить и дождаться ответа (до 15 с). null — дошло, иначе — почему нет (без токена).</summary>
    public static string? Send(string token, string chatId, string text)
    {
        try
        {
            using var web = new TimeoutWebClient { Encoding = Encoding.UTF8 };
            web.UploadValues($"https://api.telegram.org/bot{token}/sendMessage",
                new NameValueCollection { ["chat_id"] = chatId, ["text"] = text });
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
        foreach (var (token, chatId, text) in _queue.GetConsumingEnumerable())
        {
            if (Send(token, chatId, text) is { } problem)
                _log.Warning($"Уведомление в Telegram не отправлено: {problem}");
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

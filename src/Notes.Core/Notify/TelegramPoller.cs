using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Notes.Core.Settings;

namespace Notes.Core.Notify;

/// <summary>
/// Команды из Telegram: long polling getUpdates в своём фоновом потоке, один на окно (у одного токена слушать может только
/// одна программа — вторая получит 409). Запросы — не чаще раза в секунду; Telegram держит запрос до
/// <see cref="LongPollSeconds"/> с, пока не придёт сообщение, — так ответ приходит сразу, а запросов мало.
/// <para>
/// Принимает только сообщения из chat_id настроек: бота может найти кто угодно, остальных пропускаем. Что пришло, пока бот не
/// слушал (окно было закрыто, токен сменили), — не выполняем: старая команда «шепни» через час никому не нужна.
/// </para>
/// Токен и chat_id читаются перед каждым запросом: правка в окне действует со следующего.
/// </summary>
public sealed class TelegramPoller : IDisposable
{
    public const int LongPollSeconds = 25;

    private static readonly TimeSpan MinInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ErrorWait = TimeSpan.FromSeconds(5);
    // Токен слушает другая копия бота: она может слушать долго — спрашиваем редко, чтобы не мешать ей
    private static readonly TimeSpan ConflictWait = TimeSpan.FromSeconds(30);

    /// <summary>getUpdates: токен, offset, сколько секунд ждать → ответ Telegram (JSON и при ошибке HTTP).</summary>
    public delegate string Fetch(string token, long offset, int timeoutSeconds);

    private readonly Func<ChatNotifySettings> _settings;
    private readonly Action<TelegramMessage> _received;
    private readonly Action<string> _info;
    private readonly Action<string> _warn;
    private readonly Fetch _fetch;
    private readonly ManualResetEvent _stop = new(false);
    private volatile bool _stopped;
    private volatile HttpWebRequest? _request;

    // .NET Framework по умолчанию может не включать TLS 1.2, а Telegram без него не пускает
    static TelegramPoller() => ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

    private string? _token;
    // Следующее обновление; null — ещё не знаем, с какого: первый запрос только пропускает старое
    private long? _offset;
    private string? _problem;
    private bool _listening;

    /// <param name="settings">Токен и chat_id (читаются в фоновом потоке перед каждым запросом).</param>
    /// <param name="received">Сообщение из нашего чата (в фоновом потоке).</param>
    /// <param name="info">Начали слушать или снова на связи.</param>
    /// <param name="warn">Что-то не так — пишется один раз, пока не наладится.</param>
    /// <param name="fetch">Запрос к Telegram; null — настоящий.</param>
    public TelegramPoller(Func<ChatNotifySettings> settings, Action<TelegramMessage> received, Action<string> info, Action<string> warn,
        Fetch? fetch = null)
    {
        _settings = settings;
        _received = received;
        _info = info;
        _warn = warn;
        _fetch = fetch ?? GetUpdates;
    }

    public void Start() => new Thread(Run) { IsBackground = true, Name = "Telegram: команды" }.Start();

    private void Run()
    {
        while (!_stopped)
        {
            var started = Stopwatch.StartNew();
            TimeSpan wait;
            try
            {
                wait = Step();
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                if (_stopped)
                    return;
                Problem($"Команды из Telegram: сбой ({e.GetBaseException().Message})");
                wait = ErrorWait;
            }

            var left = MinInterval - started.Elapsed;
            _stop.WaitOne(wait > left ? wait : left < TimeSpan.Zero ? TimeSpan.Zero : left);
        }
    }

    /// <summary>Один запрос: разобрать, отдать сообщения. Ответ — сколько ждать до следующего (не меньше секунды — сам цикл).</summary>
    public TimeSpan Step()
    {
        var s = _settings();
        if (!s.CanSend)
        {
            _token = null;
            return MinInterval;
        }

        var (token, chatId) = (s.Token, s.ChatId);
        if (token != _token)
        {
            _token = token;
            _offset = null;
            _listening = false;
        }

        // Первый запрос: offset = -1 — только последнее обновление, всё до него Telegram забывает; ждать не нужно
        var first = _offset is null;
        string json;
        try
        {
            json = _fetch(token, _offset ?? -1, first ? 0 : LongPollSeconds);
        }
        catch (WebException e)
        {
            if (_stopped)
                return TimeSpan.Zero;
            Problem($"Команды из Telegram: нет связи ({e.Status})");
            return ErrorWait;
        }

        var updates = TelegramUpdates.Parse(json, chatId);
        if (updates.Error is { } error)
        {
            Problem(updates.ErrorCode switch
            {
                409 => "Команды из Telegram не принимаю: этот токен уже слушает другая программа (другая копия бота?)",
                401 or 404 => "Команды из Telegram не принимаю: Telegram не принял токен",
                _ => $"Команды из Telegram: {error}",
            });
            return updates.ErrorCode == 409 ? ConflictWait : ErrorWait;
        }

        if (updates.LastUpdateId is { } last)
            _offset = last + 1;
        else if (first)
            _offset = 0;

        if (_problem is not null || !_listening)
        {
            _info(_problem is not null ? "Команды из Telegram: снова на связи" : "Команды из Telegram: слушаю ваш чат");
            _problem = null;
            _listening = true;
        }

        if (first)
            return TimeSpan.Zero;
        if (updates.Foreign > 0)
            _info($"Telegram: сообщений не из вашего чата — {updates.Foreign}, пропущены");
        foreach (var message in updates.Messages)
            _received(message);
        return TimeSpan.Zero;
    }

    private void Problem(string text)
    {
        if (text != _problem)
            _warn(text);
        _problem = text;
    }

    private string GetUpdates(string token, long offset, int timeoutSeconds)
    {
        var request = (HttpWebRequest)WebRequest.Create(
            $"https://api.telegram.org/bot{token}/getUpdates?offset={offset}&timeout={timeoutSeconds}&allowed_updates=%5B%22message%22%5D");
        // Telegram держит запрос timeoutSeconds — ждём чуть дольше
        request.Timeout = (timeoutSeconds + 15) * 1000;
        request.ReadWriteTimeout = request.Timeout;
        _request = request;
        try
        {
            using var response = (HttpWebResponse)request.GetResponse();
            return Read(response);
        }
        catch (WebException e) when (e.Response is HttpWebResponse response)
        {
            // 401, 409…: Telegram объясняет в теле ответа
            using (response)
                return Read(response);
        }
        finally
        {
            _request = null;
        }
    }

    private static string Read(HttpWebResponse response)
    {
        using var reader = new StreamReader(response.GetResponseStream() ?? Stream.Null, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        _stopped = true;
        _stop.Set();
        _request?.Abort();
    }
}

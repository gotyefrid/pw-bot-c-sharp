using System;
using System.Collections.Generic;
using System.Linq;
using Notes.Core.Notify;
using Notes.Core.Settings;
using Xunit;

namespace Notes.Tests.Notify;

/// <summary>Команды из Telegram: разбор getUpdates, что слушатель берёт, что пропускает и как считает offset.</summary>
public class TelegramPollerTests
{
    private const string Chat = "42";

    private readonly ChatNotifySettings _settings = new() { Token = "123:abc", ChatId = Chat };
    private readonly Queue<string> _answers = new();
    private readonly List<(long Offset, int Timeout)> _asked = [];
    private readonly List<TelegramMessage> _received = [];
    private readonly List<string> _warnings = [];
    private readonly TelegramPoller _poller;

    public TelegramPollerTests()
        => _poller = new TelegramPoller(() => _settings, _received.Add, _ => { }, _warnings.Add, (_, offset, timeout) =>
        {
            _asked.Add((offset, timeout));
            return _answers.Count > 0 ? _answers.Dequeue() : """{ "ok": true, "result": [] }""";
        });

    private static string Message(long update, long id, string text, long chat = 42, string reply = "")
        => $$"""{ "update_id": {{update}}, "message": { "message_id": {{id}}, "chat": { "id": {{chat}}, "type": "private" }, "text": "{{text}}"{{reply}} } }""";

    private static string Ok(params string[] updates) => $$"""{ "ok": true, "result": [ {{string.Join(", ", updates)}} ] }""";

    [Fact]
    public void ParseTakesOurChatOnlyButCountsEveryUpdate()
    {
        var updates = TelegramUpdates.Parse(Ok(Message(7, 1, "/list"), Message(8, 2, "привет", chat: 99)), Chat);

        Assert.Equal([new TelegramMessage(1, "/list")], updates.Messages);
        Assert.Equal(8, updates.LastUpdateId);
        Assert.Equal(1, updates.Foreign);
    }

    [Fact]
    public void ParseKeepsWhatTheMessageAnswers()
    {
        var reply = """, "reply_to_message": { "message_id": 5, "text": "Comeback — лично от Странник:\nпривет" }""";

        var message = TelegramUpdates.Parse(Ok(Message(3, 6, "и тебе", reply: reply)), Chat).Messages.Single();

        Assert.Equal(5, message.ReplyToId);
        Assert.Equal("Comeback — лично от Странник:\nпривет", message.ReplyToText);
    }

    [Fact]
    public void ParseReportsTelegramError()
    {
        var updates = TelegramUpdates.Parse("""{ "ok": false, "error_code": 409, "description": "Conflict: terminated by other getUpdates request" }""", Chat);

        Assert.Equal(409, updates.ErrorCode);
        Assert.Empty(updates.Messages);
    }

    [Fact]
    public void FirstRequestSkipsWhatCameBeforeThenLongPollsFromNext()
    {
        _answers.Enqueue(Ok(Message(10, 1, "/list — старая")));
        _answers.Enqueue(Ok(Message(11, 2, "/list")));

        _poller.Step();
        _poller.Step();
        _poller.Step();

        Assert.Equal([(-1L, 0), (11L, TelegramPoller.LongPollSeconds), (12L, TelegramPoller.LongPollSeconds)], _asked);
        Assert.Equal(["/list"], _received.Select(m => m.Text));
    }

    [Fact]
    public void NothingBeforeStartMeansFromTheBeginning()
    {
        _poller.Step();
        _answers.Enqueue(Ok(Message(1, 1, "/list")));
        _poller.Step();

        Assert.Equal(0, _asked[1].Offset);
        Assert.Single(_received);
    }

    [Fact]
    public void WithoutTokenNoRequests()
    {
        _settings.Token = "";

        _poller.Step();

        Assert.Empty(_asked);
    }

    [Fact]
    public void NewTokenSkipsOldAgain()
    {
        _answers.Enqueue(Ok(Message(10, 1, "a")));
        _poller.Step();
        _settings.Token = "456:def";

        _poller.Step();

        Assert.Equal(-1, _asked[1].Offset);
    }

    [Fact]
    public void ConflictIsReportedOnceAndWaitsLonger()
    {
        var conflict = """{ "ok": false, "error_code": 409, "description": "Conflict" }""";
        _answers.Enqueue(conflict);
        _answers.Enqueue(conflict);

        var wait = _poller.Step();
        _poller.Step();

        Assert.Single(_warnings);
        Assert.Contains("другая программа", _warnings[0]);
        Assert.True(wait >= TimeSpan.FromSeconds(10));
    }
}

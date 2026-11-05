using System.Collections.Generic;
using System.Linq;
using Notes.Core.Notify;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Notify;

/// <summary>Уведомления в Telegram о сообщениях в чате: о каких писать и о каких нет. Каналы и тексты — как на Comeback 1.3.6.</summary>
public class ChatNotifierTests
{
    private const uint Friend = 0x00A6_44F0;
    private readonly FakeWorld _world = new();
    private readonly List<string> _sent = [];
    private readonly ChatNotifySettings _settings = new() { Token = "123:abc", ChatId = "42" };
    private readonly ChatNotifier _notifier;

    public ChatNotifierTests()
    {
        var channels = new ChatChannels { Common = 0, Group = 2, Private = 4 };
        _notifier = new ChatNotifier(channels, () => _settings, _sent.Add);
        _world.Chat.Add(new ChatMessage(1, 1, "Старое: в мире", 0x0009_7F10));
    }

    private void Say(int channel, string text, uint sender)
        => _world.Chat.Add(new ChatMessage(_world.Chat.Max(m => m.Number) + 1, channel, text, sender));

    private void Observe() => _notifier.Observe(_world.Snapshot());

    private void Start()
    {
        _notifier.Active = true;
        Observe();
    }

    [Fact]
    public void PrivateMessageIsSentWithSender()
    {
        Start();
        Say(4, "^00FFFFТихоня шепчет: привет", Friend);
        Observe();

        Assert.Equal(["Перс — лично от Тихоня:\nпривет"], _sent);
    }

    [Fact]
    public void HistoryBeforeStartIsNotSent()
    {
        Say(4, "Странник шепчет: давно было", Friend);
        Observe();
        Start();
        Observe();

        Assert.Empty(_sent);
    }

    [Fact]
    public void NothingIsSentWhileBotIsStopped()
    {
        Observe();
        Say(4, "Странник шепчет: привет", Friend);
        Observe();

        Assert.Empty(_sent);
    }

    [Fact]
    public void OwnAndSystemMessagesAreNotSent()
    {
        _settings.Group = true;
        Start();
        Say(4, "Вы говорите с Странник: написал в личку", uint.MaxValue);
        Say(2, "Перс: иду", FakeWorld.HostWid);
        Say(8, "Тест сейчас вне игры.", uint.MaxValue);
        Observe();

        Assert.Empty(_sent);
    }

    [Fact]
    public void OnlyChosenChannels()
    {
        _settings.Private = false;
        _settings.Common = true;
        Start();
        Say(4, "Странник шепчет: привет", Friend);
        Say(2, "Странник: в группу", Friend);
        Say(0, "Странник: в общий", Friend);
        Say(1, "Странник: в мир", Friend);
        Observe();

        Assert.Equal(["Перс — общий от Странник:\nв общий"], _sent);
    }

    [Fact]
    public void UnverifiedChannelIsNotSentEvenIfChosen()
    {
        _settings.Clan = true;
        Start();
        Say(3, "Странник: в клан", Friend);
        Observe();

        Assert.Empty(_sent);
    }

    [Fact]
    public void NothingIsSentWithoutTokenOrChatId()
    {
        _settings.ChatId = "";
        Start();
        Say(4, "Странник шепчет: привет", Friend);
        Observe();

        Assert.Empty(_sent);
    }

    [Fact]
    public void RestartSkipsWhatCameWhileStopped()
    {
        Start();
        _notifier.Active = false;
        Say(4, "Странник шепчет: пока стоял", Friend);
        Observe();
        Start();
        Say(4, "Странник шепчет: после старта", Friend);
        Observe();

        Assert.Equal(["Перс — лично от Странник:\nпосле старта"], _sent);
    }

    [Fact]
    public void EachMessageIsSentOnce()
    {
        Start();
        Say(4, "Странник шепчет: привет", Friend);
        Observe();
        Observe();

        Assert.Single(_sent);
    }

    [Theory]
    [InlineData("Тихоня шепчет: привет", "Тихоня", "привет")]
    [InlineData("Странник: написал в группу: важно", "Странник", "написал в группу: важно")]
    [InlineData("без двоеточия", null, "без двоеточия")]
    public void SenderIsTakenFromText(string plain, string? from, string text)
    {
        Assert.Equal((from, text), ChatNotifier.Split(plain));
    }
}

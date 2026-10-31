using System.Collections.Generic;
using System.Linq;
using Notes.Core.Logging;
using Notes.Core.World;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.World;

/// <summary>Кто убил — по фразе игры в чате после смерти.</summary>
public class KillerLogTests
{
    private const string KilledBy = "%s. Запомните это имя. Так зовут вашего убийцу.";
    private readonly FakeWorld _world = new();
    private readonly List<LogEntry> _log = [];
    private readonly List<ChatMessage> _chat = [new(100, 1, "^FFE400&Geranika&: 3гр", 0x0012_3456)];
    private readonly KillerLog _killer;

    private sealed class ListSink(List<LogEntry> entries) : ILogSink
    {
        public void Write(LogEntry entry) => entries.Add(entry);
    }

    public KillerLogTests() => _killer = new KillerLog(new Logger().AddSink(new ListSink(_log)).For(LogSources.Brain), KilledBy);

    private void Say(string text) => _chat.Add(new ChatMessage(_chat.Max(m => m.Number) + 1, 8, text, uint.MaxValue));

    private List<string> Lines()
    {
        _killer.Observe(_world.Snapshot() with { Chat = _chat.ToList() });
        return _log.Select(e => e.Message).ToList();
    }

    [Fact]
    public void KillerFromInvisibilityIsNamedWithClass()
    {
        _world.Players.Add(new PlayerInfo(1, 0x0120_7510, "Itori", new Position(15, 0, 0)) { Class = PlayerClasses.Assassin });
        Lines();
        Say("^BED293Itori. Запомните это имя. Так зовут вашего убийцу.");

        Assert.Equal(["Вас убил игрок Itori (убийца)"], Lines());
        Assert.All(_log, e => Assert.Equal(LogLevel.Warning, e.Level));
    }

    [Fact]
    public void MobIsNamedAsInChat()
    {
        Lines();
        Say("^C0C0C0Пустынный разбойник наносит вам 879 ед. урона.");
        Say("^BED293Пустынный разбойник. Запомните это имя. Так зовут вашего убийцу.");

        Assert.Equal(["Вас убил Пустынный разбойник"], Lines());
    }

    [Fact]
    public void OldMessagesAreNotRepeated()
    {
        // Смерть была до подключения — уже в чате; то же сообщение во втором снимке — не новое
        Say("^BED293Itori. Запомните это имя. Так зовут вашего убийцу.");
        Lines();
        Say("^FFE400&Geranika&: ещё");
        Lines();

        Assert.Empty(Lines());
    }

    [Fact]
    public void NewSessionStartsFromScratch()
    {
        // Перезаход: номера сообщений пошли с начала — старый «последний» не годится
        Lines();
        _chat.Clear();
        _chat.Add(new ChatMessage(1, 1, "привет", 0x0012_3456));
        Lines();
        Say("^BED293Моб. Запомните это имя. Так зовут вашего убийцу.");

        Assert.Equal(["Вас убил Моб"], Lines());
    }

    [Fact]
    public void NoPhraseInProfileMeansNothing()
    {
        var log = new List<LogEntry>();
        var killer = new KillerLog(new Logger().AddSink(new ListSink(log)).For(LogSources.Brain), "");
        killer.Observe(_world.Snapshot() with { Chat = _chat.ToList() });
        Say("^BED293Itori. Запомните это имя. Так зовут вашего убийцу.");
        killer.Observe(_world.Snapshot() with { Chat = _chat.ToList() });

        Assert.Empty(log);
    }
}

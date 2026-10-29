using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Notes.Core.Clients;
using Xunit;

namespace Notes.Tests.Clients;

public class ClientListTests
{
    private sealed class FakeSource(params (int Pid, string? Nick)[] clients) : IClientSource
    {
        public IReadOnlyList<ClientProcess> FindProcesses()
            => clients.Select(c => new ClientProcess(c.Pid, new IntPtr(c.Pid * 10), null)).ToList();

        public string? ReadNick(int pid) => clients.First(c => c.Pid == pid).Nick;
    }

    [Fact]
    public void ClientsAreSortedByPidWithNicks()
    {
        var list = ClientList.Build(new FakeSource((300, "Персонаж"), (100, null), (200, "Лучница")));

        Assert.Equal([100, 200, 300], list.Select(c => c.Pid));
        Assert.Equal("PID 100 — персонаж не в мире", list[0].Display);
        Assert.Equal("Персонаж (PID 300)", list[2].Display);
        Assert.Equal(new IntPtr(3000), list[2].Window);
    }

    [Theory]
    [InlineData("ElementClient Window", true)]
    // Другие окна процесса игры и окно самого бота (WPF)
    [InlineData("GDI+ Hook Window Class", false)]
    [InlineData("HwndWrapper[Notes.exe;;4f0c]", false)]
    [InlineData("", false)]
    public void ClientIsFoundByGameWindowClass(string className, bool expected)
    {
        Assert.Equal(expected, NativeWindows.IsClientWindowClass(className));
    }

    [Fact]
    public void ProcessWithoutGameWindowIsNotClient()
    {
        Assert.DoesNotContain(Process.GetCurrentProcess().Id, NativeWindows.FindClientProcesses());
    }

    [Theory]
    [InlineData("Персонаж", 13840, "Персонаж 13840")]
    [InlineData(null, 13840, "13840")]
    [InlineData("", 13840, "13840")]
    public void WindowTitleIsNickAndPid(string? nick, int pid, string title)
    {
        Assert.Equal(title, WindowTitles.For(nick, pid));
    }

    [Fact]
    public void RefreshKeepsSelectedClient()
    {
        var list = ClientList.Build(new FakeSource((100, "А"), (200, "Б")));

        Assert.Equal(200, ClientList.KeepSelection(list, 200)!.Pid);
    }

    [Fact]
    public void ClosedClientFallsBackToFirst()
    {
        var list = ClientList.Build(new FakeSource((100, "А"), (300, "В")));

        Assert.Equal(100, ClientList.KeepSelection(list, 200)!.Pid);
        Assert.Equal(100, ClientList.KeepSelection(list, null)!.Pid);
        Assert.Null(ClientList.KeepSelection([], 200));
    }

    [Fact]
    public void OnStartLastCharacterThenReadableNickAmongFreeClients()
    {
        // 100 — клиент другого сервера (ник не читается этим профилем), 200 — занят другой копией приложения
        var list = ClientList.Build(new FakeSource((100, null), (200, "Персонаж"), (300, "Воин"), (400, "Лучница")));
        bool Taken(int pid) => pid == 200;

        Assert.Equal(400, ClientList.KeepSelection(list, null, "Лучница", Taken)!.Pid);
        Assert.Equal(300, ClientList.KeepSelection(list, null, "Персонаж", Taken)!.Pid);
        Assert.Equal(300, ClientList.KeepSelection(list, null, "", Taken)!.Pid);
        // Подключённый остаётся, даже если он «занят» — это наша же метка
        Assert.Equal(200, ClientList.KeepSelection(list, 200, "Лучница", Taken)!.Pid);
        // Все заняты — первый
        Assert.Equal(100, ClientList.KeepSelection(list, null, "x", _ => true)!.Pid);
    }

    private const string Game = @"D:\Game\ElementClient.exe";

    // Клиенты с путём exe; запоминает, чьи ники читали
    private sealed class PathSource(params (int Pid, string? Nick, string? Exe)[] clients) : IClientSource
    {
        public List<int> NickReads { get; } = new();

        public IReadOnlyList<ClientProcess> FindProcesses()
            => clients.Select(c => new ClientProcess(c.Pid, IntPtr.Zero, c.Exe)).ToList();

        public string? ReadNick(int pid)
        {
            NickReads.Add(pid);
            return clients.First(c => c.Pid == pid).Nick;
        }
    }

    [Fact]
    public void WithGameOnlyItsExeIsListedAndOthersAreNotRead()
    {
        // 200 — второй exe в той же папке, 400 — путь не прочитать (права)
        var source = new PathSource((300, "В", Game.ToUpperInvariant()), (100, "А", Game),
            (200, "Б", @"D:\Game\ElementClient_test.exe"), (400, null, null));

        var list = ClientList.Build(source, source.FindProcesses(), Game);

        Assert.Equal([100, 300], list.Select(c => c.Pid));
        Assert.Equal([100, 300], source.NickReads);
    }

    [Fact]
    public void WithoutGameEveryClientIsListed()
    {
        var source = new PathSource((100, "А", Game), (200, "Б", @"E:\Other\client.exe"), (300, null, null));

        Assert.Equal([100, 200, 300], ClientList.Build(source, source.FindProcesses(), null).Select(c => c.Pid));
    }

    [Theory]
    [InlineData(Game, Game, true)]
    [InlineData(@"d:\game\elementclient.exe", Game, true)]
    [InlineData(@"D:\Game\ElementClient_test.exe", Game, false)]
    [InlineData(null, Game, false)]
    [InlineData(@"E:\Other\client.exe", null, true)]
    [InlineData(null, null, true)]
    public void ClientIsOfGameByExactExe(string? exe, string? game, bool expected)
    {
        Assert.Equal(expected, ClientList.IsOfGame(exe, game));
    }

    [Fact]
    public void GameIsGuessedOnlyWhenServerClientsShareOneExe()
    {
        static GameClient C(int pid, string? nick, string? exe) => new(pid, nick, IntPtr.Zero, exe);

        // Клиент другого сервера (ник этим профилем не читается) не мешает
        Assert.Equal(Game, ClientList.GuessGame([C(1, "А", Game), C(2, "Б", Game.ToLowerInvariant()), C(3, null, @"E:\Other\client.exe")]));
        // Два exe — «Игрой» станет тот, к кому подключатся
        Assert.Null(ClientList.GuessGame([C(1, "А", Game), C(2, "Б", @"D:\Game\ElementClient_test.exe")]));
        // Путь не прочитан — не угадываем
        Assert.Null(ClientList.GuessGame([C(1, "А", Game), C(2, "Б", null)]));
        Assert.Null(ClientList.GuessGame([C(1, "А", null)]));
        // Ник не читается ни у кого — профиль, может, не подходит
        Assert.Null(ClientList.GuessGame([C(1, null, Game)]));
        Assert.Null(ClientList.GuessGame([]));
    }

    [Fact]
    public void ClientLockMarksClientForOtherWindows()
    {
        var pid = 900000 + Environment.TickCount % 1000;
        Assert.False(ClientLock.IsTaken(pid));
        using (var mine = ClientLock.TryTake(pid))
        {
            Assert.NotNull(mine);
            Assert.True(ClientLock.IsTaken(pid));
            Assert.Null(ClientLock.TryTake(pid));
        }
        Assert.False(ClientLock.IsTaken(pid));
    }
}

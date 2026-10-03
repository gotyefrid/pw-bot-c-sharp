using System;
using System.Collections.Generic;
using System.Linq;
using BotCH.Core.Clients;
using Xunit;

namespace BotCH.Tests.Clients;

public class ClientListTests
{
    private sealed class FakeSource(params (int Pid, string? Nick)[] clients) : IClientSource
    {
        public IReadOnlyList<(int Pid, IntPtr Window)> FindProcesses(string processName)
            => clients.Select(c => (c.Pid, new IntPtr(c.Pid * 10))).ToList();

        public string? ReadNick(int pid) => clients.First(c => c.Pid == pid).Nick;
    }

    [Fact]
    public void ClientsAreSortedByPidWithNicks()
    {
        var list = ClientList.Build(new FakeSource((300, "Купчихан"), (100, null), (200, "Лучница")), "elementclient");

        Assert.Equal([100, 200, 300], list.Select(c => c.Pid));
        Assert.Equal("PID 100 — персонаж не в мире", list[0].Display);
        Assert.Equal("Купчихан (PID 300)", list[2].Display);
        Assert.Equal(new IntPtr(3000), list[2].Window);
    }

    [Theory]
    [InlineData("Купчихан", 13840, "Купчихан 13840")]
    [InlineData(null, 13840, "13840")]
    [InlineData("", 13840, "13840")]
    public void WindowTitleIsNickAndPid(string? nick, int pid, string title)
    {
        Assert.Equal(title, WindowTitles.For(nick, pid));
    }

    [Fact]
    public void RefreshKeepsSelectedClient()
    {
        var list = ClientList.Build(new FakeSource((100, "А"), (200, "Б")), "elementclient");

        Assert.Equal(200, ClientList.KeepSelection(list, 200)!.Pid);
    }

    [Fact]
    public void ClosedClientFallsBackToFirst()
    {
        var list = ClientList.Build(new FakeSource((100, "А"), (300, "В")), "elementclient");

        Assert.Equal(100, ClientList.KeepSelection(list, 200)!.Pid);
        Assert.Equal(100, ClientList.KeepSelection(list, null)!.Pid);
        Assert.Null(ClientList.KeepSelection([], 200));
    }

    [Fact]
    public void OnStartLastCharacterThenReadableNickAmongFreeClients()
    {
        // 100 — клиент другого сервера (ник не читается этим профилем), 200 — занят другим окном BotCH
        var list = ClientList.Build(new FakeSource((100, null), (200, "Купчихан"), (300, "ClaudeCot"), (400, "Лучница")), "elementclient");
        bool Taken(int pid) => pid == 200;

        Assert.Equal(400, ClientList.KeepSelection(list, null, "Лучница", Taken)!.Pid);
        Assert.Equal(300, ClientList.KeepSelection(list, null, "Купчихан", Taken)!.Pid);
        Assert.Equal(300, ClientList.KeepSelection(list, null, "", Taken)!.Pid);
        // Подключённый остаётся, даже если он «занят» — это наша же метка
        Assert.Equal(200, ClientList.KeepSelection(list, 200, "Лучница", Taken)!.Pid);
        // Все заняты — первый
        Assert.Equal(100, ClientList.KeepSelection(list, null, "x", _ => true)!.Pid);
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

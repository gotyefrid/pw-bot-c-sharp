using System;
using System.Collections.Generic;
using BotCH.Core.Calls;
using Xunit;

namespace BotCH.Tests.Calls;

public class WindowLossWatchTests
{
    private sealed class Runner : IRemoteRunner
    {
        public Queue<RemoteRunStatus> Next { get; } = new();

        public RemoteRunResult Run(byte[]? data, Func<uint, byte[]> buildStub) => new(Next.Dequeue());
    }

    private readonly Runner _runner = new();

    private WindowLossWatch Watch(params RemoteRunStatus[] statuses)
    {
        foreach (var status in statuses)
            _runner.Next.Enqueue(status);
        var watch = new WindowLossWatch(_runner);
        for (var i = 0; i < statuses.Length; i++)
            watch.Run(null, _ => []);
        return watch;
    }

    [Fact]
    public void ThreeLossesInARowBreak()
        => Assert.Equal("связь с окном игры потеряна",
            Watch(RemoteRunStatus.WindowLost, RemoteRunStatus.WindowLost, RemoteRunStatus.WindowLost).Broken);

    [Fact]
    public void TwoLossesAreNotYetBroken()
        => Assert.Null(Watch(RemoteRunStatus.WindowLost, RemoteRunStatus.WindowLost).Broken);

    [Fact]
    public void AnyOtherResultStartsCountingAgain()
        => Assert.Null(Watch(RemoteRunStatus.WindowLost, RemoteRunStatus.WindowLost, RemoteRunStatus.Done, RemoteRunStatus.WindowLost,
            RemoteRunStatus.WindowLost, RemoteRunStatus.Timeout, RemoteRunStatus.WindowLost).Broken);
}

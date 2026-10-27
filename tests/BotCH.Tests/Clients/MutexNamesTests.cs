using BotCH.Core.Clients;
using Xunit;

namespace BotCH.Tests.Clients;

public class MutexNamesTests
{
    [Fact]
    public void SameKeyGivesSameName()
        => Assert.Equal(MutexNames.For("app", @"d:/bots/farm"), MutexNames.For("app", @"d:/bots/farm"));

    [Fact]
    public void PurposeAndKeyChangeName()
    {
        Assert.NotEqual(MutexNames.For("app", "12"), MutexNames.For("client", "12"));
        Assert.NotEqual(MutexNames.For("client", "12"), MutexNames.For("client", "13"));
    }

    [Fact]
    public void NameHasNeitherBotNorFolder()
    {
        var name = MutexNames.For("app", @"d:/bots/botch-farm");

        Assert.StartsWith(@"Local\", name);
        Assert.DoesNotContain("botch", name, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("farm", name);
        Assert.Matches(@"^Local[\\][0-9a-f]{16}$", name);
    }
}

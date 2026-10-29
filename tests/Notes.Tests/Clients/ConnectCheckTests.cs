using Notes.Core.Clients;
using Xunit;
using static Notes.Core.Clients.ClientPatchState;

namespace Notes.Tests.Clients;

/// <summary>Проверка перед «Подключить»: процесс, папка игры, патч → вердикт и подсказка.</summary>
public class ConnectCheckTests
{
    private const string Game = @"D:\Game\ElementClient.exe";

    [Fact]
    public void ClosedClientIsGoneEvenWithoutRecipe()
        => Assert.Equal(ConnectVerdict.ProcessGone, ConnectCheck.Decide(false, null, null, alive: false, Missing).Verdict);

    [Fact]
    public void ServerWithoutRecipeIsReady()
        => Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(false, null, Game, alive: true, Missing).Verdict);

    [Fact]
    public void PatchedIsReady()
    {
        var decision = ConnectCheck.Decide(true, Game, Game, alive: true, Patched);
        Assert.Equal(ConnectVerdict.Ready, decision.Verdict);
        Assert.Equal("", decision.Text);
    }

    [Theory]
    [InlineData(Original, ConnectVerdict.NotPatched)]
    [InlineData(Unknown, ConnectVerdict.UnknownBuild)]
    [InlineData(Missing, ConnectVerdict.UnknownBuild)]
    [InlineData(Unreadable, ConnectVerdict.UnknownBuild)]
    public void UnpatchedAsksForNextStep(ClientPatchState state, ConnectVerdict expected)
    {
        var decision = ConnectCheck.Decide(true, Game, Game, alive: true, state);
        Assert.Equal(expected, decision.Verdict);
        Assert.NotEqual("", decision.Text);
    }

    [Theory]
    [InlineData(Original)]
    [InlineData(Unknown)]
    [InlineData(Missing)]
    [InlineData(Unreadable)]
    public void AnywaySkipsOnlyThePatchCheck(ClientPatchState state)
    {
        Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(true, Game, Game, alive: true, state, anyway: true).Verdict);
        Assert.Equal(ConnectVerdict.ProcessGone, ConnectCheck.Decide(true, Game, Game, alive: false, state, anyway: true).Verdict);
        Assert.Equal(ConnectVerdict.OtherInstall, ConnectCheck.Decide(true, Game, @"E:\Test\ElementClient.exe", alive: true, state, anyway: true).Verdict);
    }

    [Fact]
    public void OtherFolderIsReportedWithItsPathAndIgnoresCase()
    {
        var other = ConnectCheck.Decide(true, Game, @"E:\Test\ElementClient.exe", alive: true, Patched);
        Assert.Equal(ConnectVerdict.OtherInstall, other.Verdict);
        Assert.Contains(@"E:\Test\ElementClient.exe", other.Text);

        Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(true, Game.ToLowerInvariant(), Game, alive: true, Patched).Verdict);
    }

    [Fact]
    public void UnknownClientPathIsNotAnotherFolder()
        => Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(true, Game, null, alive: true, Patched).Verdict);
}

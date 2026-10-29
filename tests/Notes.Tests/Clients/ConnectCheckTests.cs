using Notes.Core.Clients;
using Xunit;
using static Notes.Core.Clients.ClientPatchState;

namespace Notes.Tests.Clients;

/// <summary>Проверка перед «Подключить»: процесс, патч → вердикт и подсказка.</summary>
public class ConnectCheckTests
{
    [Fact]
    public void ClosedClientIsGoneEvenWithoutRecipe()
        => Assert.Equal(ConnectVerdict.ProcessGone, ConnectCheck.Decide(false, alive: false, Missing).Verdict);

    [Fact]
    public void ServerWithoutRecipeIsReady()
        => Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(false, alive: true, Missing).Verdict);

    [Fact]
    public void PatchedIsReady()
    {
        var decision = ConnectCheck.Decide(true, alive: true, Patched);
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
        var decision = ConnectCheck.Decide(true, alive: true, state);
        Assert.Equal(expected, decision.Verdict);
        Assert.NotEqual("", decision.Text);
    }

    [Theory]
    [InlineData(Original)]
    [InlineData(Unknown)]
    [InlineData(Missing)]
    [InlineData(Unreadable)]
    public void AnywaySkipsThePatchCheckButNotClosedClient(ClientPatchState state)
    {
        Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(true, alive: true, state, anyway: true).Verdict);
        Assert.Equal(ConnectVerdict.ProcessGone, ConnectCheck.Decide(true, alive: false, state, anyway: true).Verdict);
    }
}

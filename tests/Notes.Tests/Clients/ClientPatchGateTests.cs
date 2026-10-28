using Notes.Core.Clients;
using Xunit;
using static Notes.Core.Clients.ClientPatchPolicy;
using static Notes.Core.Clients.ClientPatchState;

namespace Notes.Tests.Clients;

/// <summary>Ворота подключения: политика × состояние файла → пустить, спросить или отказать.</summary>
public class ClientPatchGateTests
{
    [Theory]
    [InlineData(Ask, Patched, ClientPatchVerdict.Allow)]
    [InlineData(Ask, Original, ClientPatchVerdict.Ask)]
    [InlineData(Ask, Unknown, ClientPatchVerdict.Ask)]
    [InlineData(Ask, Missing, ClientPatchVerdict.Ask)]
    [InlineData(Ask, Unreadable, ClientPatchVerdict.Ask)]
    [InlineData(Require, Patched, ClientPatchVerdict.Allow)]
    [InlineData(Require, Original, ClientPatchVerdict.Refuse)]
    [InlineData(Require, Unknown, ClientPatchVerdict.Refuse)]
    [InlineData(Require, Missing, ClientPatchVerdict.Refuse)]
    [InlineData(Require, Unreadable, ClientPatchVerdict.Refuse)]
    [InlineData(Ignore, Patched, ClientPatchVerdict.Allow)]
    [InlineData(Ignore, Original, ClientPatchVerdict.Allow)]
    [InlineData(Ignore, Unknown, ClientPatchVerdict.Allow)]
    [InlineData(Ignore, Missing, ClientPatchVerdict.Allow)]
    [InlineData(Ignore, Unreadable, ClientPatchVerdict.Allow)]
    public void DecidesByPolicyAndState(ClientPatchPolicy policy, ClientPatchState state, ClientPatchVerdict expected)
        => Assert.Equal(expected, ClientPatchGate.Decide(policy, state).Verdict);

    [Fact]
    public void PreparesTextsForAskAndRefuse()
    {
        var ask = ClientPatchGate.Decide(Ask, Original);
        Assert.Contains("на свой страх и риск", ask.Text);
        Assert.NotEqual("", ask.Problem);

        var refuse = ClientPatchGate.Decide(Require, Unknown);
        Assert.Contains("Пропатчить", refuse.Text);
        Assert.Contains("обновление игры", refuse.Problem);

        // Не прочитали — не «обновление игры», а своя причина
        Assert.Contains("не читается", ClientPatchGate.Decide(Ask, Unreadable).Problem);

        var allow = ClientPatchGate.Decide(Ignore, Original);
        Assert.Equal("", allow.Text);
    }
}

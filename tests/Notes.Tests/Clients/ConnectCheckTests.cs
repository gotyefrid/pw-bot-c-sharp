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
    [InlineData(Outdated, ConnectVerdict.OutdatedPatch)]
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
    [InlineData(Outdated)]
    [InlineData(Unknown)]
    [InlineData(Missing)]
    [InlineData(Unreadable)]
    public void AnywaySkipsThePatchCheckButNotClosedClient(ClientPatchState state)
    {
        Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(true, alive: true, state, anyway: true).Verdict);
        Assert.Equal(ConnectVerdict.ProcessGone, ConnectCheck.Decide(true, alive: false, state, anyway: true).Verdict);
    }

    // ── Сторож клиента (pwprotector) ─────────────────────────────────────────

    [Theory]
    [InlineData(ProtectorState.Known)]
    [InlineData(ProtectorState.NotConfigured)]
    public void SameProtectorIsReady(ProtectorState protector)
        => Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(true, alive: true, Patched, protector: protector).Verdict);

    [Fact]
    public void NoProtectorArgumentIsReady()
        => Assert.Equal(ConnectVerdict.Ready, ConnectCheck.Decide(true, alive: true, Patched).Verdict);

    [Theory]
    [InlineData(ProtectorState.Changed, ConnectVerdict.ProtectorChanged)]
    [InlineData(ProtectorState.Missing, ConnectVerdict.ProtectorUnknown)]
    [InlineData(ProtectorState.Unreadable, ConnectVerdict.ProtectorUnknown)]
    public void UpdatedOrUnreadableProtectorAsksForConfirmation(ProtectorState protector, ConnectVerdict expected)
    {
        var decision = ConnectCheck.Decide(true, alive: true, Patched, protector: protector);
        Assert.Equal(expected, decision.Verdict);
        Assert.NotEqual("", decision.Text);
    }

    /// <summary>Сначала «клиент без патча»: про обновление сторожа игрок узнает, когда патч уже стоит.</summary>
    [Fact]
    public void PatchProblemIsReportedBeforeProtector()
        => Assert.Equal(ConnectVerdict.NotPatched,
            ConnectCheck.Decide(true, alive: true, Original, protector: ProtectorState.Changed).Verdict);

    [Fact]
    public void AnywaySkipsTheProtectorCheck()
        => Assert.Equal(ConnectVerdict.Ready,
            ConnectCheck.Decide(true, alive: true, Patched, anyway: true, protector: ProtectorState.Changed).Verdict);

    /// <summary>Сторож проверяется и у сервера без рецепта правок (PW Classic): эталон есть — проверяем.</summary>
    [Fact]
    public void ProtectorIsCheckedWithoutRecipe()
        => Assert.Equal(ConnectVerdict.ProtectorChanged,
            ConnectCheck.Decide(false, alive: true, Missing, protector: ProtectorState.Changed).Verdict);
}

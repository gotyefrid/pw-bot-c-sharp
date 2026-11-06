using Notes.Core.Clients;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Clients;

/// <summary>Экран входа по памяти клиента: окна и тексты — как их видели на живом PW Classic (Probe ui).</summary>
public class LoginScreenTests
{
    private readonly FakeLoginMemory _client = new();

    private LoginView Read() => new LoginScreen(_client.Memory, _client.Roots, _client.Profile).Read();

    [Fact]
    public void NoWindowManagerIsLoading()
    {
        Assert.Equal(LoginStage.Loading, Read().Stage);
    }

    [Fact]
    public void LoginShowsPingAccountAndFocus()
    {
        _client.Show(false, new FakeDialog("Win_LoginButton"), FakeLoginMemory.LoginWindow(account: "probetest"),
            new FakeDialog("Win_Select", false, []));

        var view = Read();

        Assert.Equal(LoginStage.Login, view.Stage);
        Assert.Equal("344 ms", view.Ping);
        Assert.Equal("probetest", view.Account);
        Assert.Equal(LoginScreen.AccountField, view.Focus);
        Assert.Null(view.Message);
        // Закрытое окно выбора — не в списке
        Assert.Equal(["Win_LoginButton", "Win_Login"], view.Dialogs);
    }

    [Fact]
    public void ErrorMessageIsItsLongestText()
    {
        _client.Show(false, FakeLoginMemory.LoginWindow(ping: ""),
            FakeLoginMemory.Message("Соединение с сервером было разорвано. Пожалуйста, попробуйте зайти еще раз."));

        var view = Read();

        Assert.Equal(LoginStage.Login, view.Stage);
        Assert.Equal("", view.Ping);
        Assert.Equal("Соединение с сервером было разорвано. Пожалуйста, попробуйте зайти еще раз.", view.Message);
    }

    [Fact]
    public void SelectListsRolesAndTheChecked()
    {
        _client.Show(false, FakeLoginMemory.Select(2, "Купец", "Флай"), new FakeDialog("Win_Manage"));

        var view = Read();

        Assert.Equal(LoginStage.Select, view.Stage);
        Assert.Equal([new LoginRole(1, "Купец", "Друид Ур. 46"), new LoginRole(2, "Флай", "Друид Ур. 46")], view.Roles);
        Assert.Equal(2, view.Selected);
    }

    [Fact]
    public void WorldWithDisconnectMessage()
    {
        _client.Show(true, new FakeDialog("Win_Chat"), FakeLoginMemory.Message("Соединение с сервером разорвано"));

        var view = Read();

        Assert.Equal(LoginStage.World, view.Stage);
        Assert.Equal("Соединение с сервером разорвано", view.Message);
    }

    [Fact]
    public void ComebackAgreementBeforeLogin()
    {
        var client = new FakeLoginMemory("comeback136");
        client.Show(false, new FakeDialog("Win_Agreement"), new FakeDialog("Win_LoginVersion"), new FakeDialog("Win_Login", false, []));

        Assert.Equal(LoginStage.Agreement, new LoginScreen(client.Memory, client.Roots, client.Profile).Read().Stage);

        // После «Принять» байт соглашения гаснет не сразу — экран логина главнее
        client.Show(false, new FakeDialog("Win_Agreement"), FakeLoginMemory.LoginWindow(focus: null));
        Assert.Equal(LoginStage.Login, new LoginScreen(client.Memory, client.Roots, client.Profile).Read().Stage);
    }

    [Fact]
    public void ComebackLoginWithoutFocusTellsWhereTheAccountFieldIs()
    {
        var client = new FakeLoginMemory("comeback136");
        client.Show(false, FakeLoginMemory.LoginWindow(account: "probetest", focus: null));

        var view = new LoginScreen(client.Memory, client.Roots, client.Profile).Read();

        Assert.Equal(LoginStage.Login, view.Stage);
        Assert.Equal("probetest", view.Account);
        Assert.Null(view.Focus);
        // Окно 718, 343 + поле 144, 65 + половина 143×23
        Assert.Equal((933, 419), view.AccountAt);
    }

    [Fact]
    public void NoPositionsInProfileNoClickPoint()
    {
        _client.Show(false, FakeLoginMemory.LoginWindow());

        Assert.Null(Read().AccountAt);
    }

    [Fact]
    public void ProfileWithoutWindowsIsUnknown()
    {
        // Новый клиент, у которого окна ещё не искали: смещения пустые
        var unknown = new Notes.Core.Profiles.ProfileData();

        Assert.False(LoginScreen.Supported(unknown));
        Assert.True(LoginScreen.Supported(_client.Profile));
        Assert.Equal(LoginStage.Unknown, new LoginScreen(_client.Memory, _client.Roots, unknown).Read().Stage);
    }
}

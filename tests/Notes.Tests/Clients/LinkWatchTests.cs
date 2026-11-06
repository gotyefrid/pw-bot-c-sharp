using System;
using System.Collections.Generic;
using Notes.Core.Clients;
using Xunit;

namespace Notes.Tests.Clients;

/// <summary>Сторож связи: окно разрыва и экран входа — после 5 с подряд, закрытый клиент — сразу, сообщает один раз.</summary>
public class LinkWatchTests
{
    private const string Broken = "Соединение с сервером было разорвано. Пожалуйста, попробуйте зайти еще раз.";

    private static readonly DateTime T0 = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private LoginView _view = World();
    private bool _alive = true;
    private readonly List<string> _lost = [];

    private LinkWatch Watch()
    {
        var watch = new LinkWatch(() => _alive, () => _view, ["Соединение с сервером было разорвано"]);
        watch.Lost += _lost.Add;
        return watch;
    }

    private static LoginView World(string? message = null) => LoginView.None with { Stage = LoginStage.World, Message = message };

    [Fact]
    public void BrokenLinkWindowAfterHold()
    {
        var watch = Watch();
        Assert.Null(watch.Check(T0));

        _view = World(Broken);
        Assert.Null(watch.Check(T0.AddSeconds(2)));
        Assert.Null(watch.Check(T0.AddSeconds(4)));
        var reason = watch.Check(T0.AddSeconds(7));

        Assert.NotNull(reason);
        Assert.Contains("связь с сервером потеряна", reason);
        Assert.Equal([reason!], _lost);
        // Дальше молчит
        Assert.Null(watch.Check(T0.AddSeconds(9)));
        Assert.Single(_lost);
    }

    [Fact]
    public void ShortGlitchIsNotALoss()
    {
        var watch = Watch();
        _view = World(Broken);
        watch.Check(T0);
        _view = World();
        watch.Check(T0.AddSeconds(3));
        _view = World(Broken);
        watch.Check(T0.AddSeconds(5));

        Assert.Null(watch.Check(T0.AddSeconds(8)));
        Assert.Empty(_lost);
    }

    [Fact]
    public void OtherMessagesInTheWorldAreNotALoss()
    {
        var watch = Watch();
        _view = World("Недостаточно места в инвентаре");

        watch.Check(T0);
        Assert.Null(watch.Check(T0.AddMinutes(1)));
    }

    [Fact]
    public void LoginScreenInsteadOfWorld()
    {
        var watch = Watch();
        _view = LoginView.None with { Stage = LoginStage.Login };
        watch.Check(T0);

        Assert.Equal("клиент вышел на экран входа", watch.Check(T0.AddSeconds(5)));
    }

    [Fact]
    public void LoadingIsNotALoss()
    {
        var watch = Watch();
        _view = LoginView.None with { Stage = LoginStage.Loading };
        watch.Check(T0);

        Assert.Null(watch.Check(T0.AddMinutes(1)));
    }

    [Fact]
    public void ClosedClientAtOnce()
    {
        var watch = Watch();
        _alive = false;

        Assert.Equal("клиент игры закрылся", watch.Check(T0));
    }

    [Fact]
    public void DisposedWatchIsSilent()
    {
        var watch = Watch();
        watch.Dispose();
        _alive = false;

        Assert.Null(watch.Check(T0));
        Assert.Empty(_lost);
    }
}

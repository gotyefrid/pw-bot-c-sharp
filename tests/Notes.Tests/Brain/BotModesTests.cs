using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

public class BotModesTests
{
    private readonly FakeActions _actions = new();

    private IBotRunner Create(BotMode mode)
        => BotModes.Create(mode, new ActionRunner(new GameControl(_actions), NullLogger.Instance), new ClassSkills(), new BotSettings(), NullLogger.Instance);

    [Fact]
    public void FarmModeIsTheBrain()
    {
        Assert.IsType<BotBrain>(Create(BotMode.FarmMobs));
        Assert.IsType<BotBrain>(Create(BotMode.GatherResources));
    }

    [Fact]
    public void OnlyGatheringReportsRouteProgress()
    {
        // Окно подсвечивает текущую точку маршрута, спрашивая возможность, а не класс режима
        Assert.Equal(0, Create(BotMode.GatherResources).Part<IRouteProgress>()?.Index);
        Assert.Null(Create(BotMode.FarmMobs).Part<IRouteProgress>());
        Assert.Null(Create(BotMode.Clicker).Part<IRouteProgress>());
    }

    [Fact]
    public void ClickerIsTheBrainToo() => Assert.IsType<BotBrain>(Create(BotMode.Clicker));

    [Fact]
    public void ModeIsSavedPerCharacterSettings()
    {
        var settings = SettingsJson.Parse("""{ "mode": "Clicker" }""");

        Assert.Equal(BotMode.Clicker, settings.Mode);
        Assert.Contains("\"mode\": \"Clicker\"", SettingsJson.Serialize(settings));
        Assert.Equal(BotMode.FarmMobs, new BotSettings().Mode);
    }

    [Fact]
    public void EveryModeHasTitle()
    {
        Assert.Equal(["Бить мобов", "Собирать ресурсы", "Кликер"],
            new[] { BotMode.FarmMobs, BotMode.GatherResources, BotMode.Clicker }.Select(BotModes.Title));
    }
}

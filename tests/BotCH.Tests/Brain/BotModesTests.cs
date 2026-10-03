using System.Linq;
using BotCH.Core.Actions;
using BotCH.Core.Brain;
using BotCH.Core.Logging;
using BotCH.Core.Profiles;
using BotCH.Core.Settings;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.Brain;

public class BotModesTests
{
    private readonly FakeActions _actions = new();

    private IBotRunner Create(BotMode mode)
        => BotModes.Create(mode, new ActionRunner(_actions, NullLogger.Instance), new ClassSkills(), new BotSettings(), NullLogger.Instance);

    [Fact]
    public void FarmModeIsTheBrain()
    {
        Assert.IsType<BotBrain>(Create(BotMode.FarmMobs));
        Assert.NotNull(Assert.IsType<BotBrain>(Create(BotMode.GatherResources)).Route);
    }

    [Theory]
    [InlineData(BotMode.Clicker, "кликер")]
    public void NotReadyModesDoNothingInGame(BotMode mode, string name)
    {
        var world = new FakeWorld();
        world.AddMob(1, "Волк", 5);
        world.Hp = 10;
        world.AddPotion(1, 8617, 5, hp: 30);
        var runner = Create(mode);

        for (var i = 0; i < 10; i++)
            runner.Tick(world.Wait(0.25).Snapshot());

        Assert.Empty(_actions.Calls);
        Assert.Contains(name, runner.Status);
    }

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

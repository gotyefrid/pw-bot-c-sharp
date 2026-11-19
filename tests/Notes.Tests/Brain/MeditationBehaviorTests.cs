using System;
using System.Linq;
using Notes.Core.Actions;
using Notes.Core.Brain;
using Notes.Core.Logging;
using Notes.Core.Profiles;
using Notes.Core.Settings;
using Notes.Tests.Fakes;
using Xunit;

namespace Notes.Tests.Brain;

/// <summary>Медитация в режиме «Бить мобов»: когда садится, когда встаёт, что при нападении.</summary>
public class MeditationBehaviorTests
{
    private static readonly ClassSkills Skills = new() { DefaultAttack = 299 };

    private readonly FakeWorld _world = new() { Hp = 500, MaxHp = 500, Mp = 500, MaxMp = 500 };
    private readonly FakeActions _actions = new();
    private readonly BotSettings _settings = new();
    private IBotRunner? _brain;

    private IBotRunner Brain => _brain ??= BotModes.Create(BotMode.FarmMobs,
        new ActionRunner(new GameControl(_actions), NullLogger.Instance), Skills, _settings, NullLogger.Instance, random: new Random(1));

    public MeditationBehaviorTests()
    {
        _settings.Target.KillMobs = false;
        _settings.Pet.Enabled = false;
        _settings.Potions.UseHp = false;
        _settings.Potions.UseMp = false;
        _settings.Target.TravelFrom = 0;
        var m = _settings.Meditation;
        m.Enabled = true;
        m.UseHp = true;
        m.HpPercent = 50;
        m.UseMp = true;
        m.MpPercent = 40;
        m.StandHpPercent = 90;
        m.StandMpPercent = 80;
    }

    private void Tick(double seconds = 0.25) => Brain.Tick(_world.Wait(seconds).Snapshot());

    private int Count(string call) => _actions.Calls.Count(c => c == call);

    [Fact]
    public void FullHpAndMpDoesNotSit()
    {
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void LowHpSits()
    {
        _world.Hp = 200; // 40 %
        Tick();
        Assert.Equal(1, Count("sit"));
    }

    [Fact]
    public void BothRequiredWaitsForMpToo()
    {
        _settings.Meditation.RequireBoth = true;
        _world.Hp = 200;
        Tick();
        Assert.Equal(0, Count("sit"));

        _world.Mp = 100; // 20 %
        Tick();
        Assert.Equal(1, Count("sit"));
    }

    [Fact]
    public void AboveCompareSitsWhenHigher()
    {
        _settings.Meditation.UseHp = false;
        _settings.Meditation.MpCompare = MeditationCompare.AtLeast;
        _settings.Meditation.MpPercent = 30;
        _world.Mp = 200; // 40 % ≥ 30 %, но до «вставать при» 80 % не дошло
        Tick();
        Assert.Equal(1, Count("sit"));
    }

    [Fact]
    public void DisabledDoesNotSit()
    {
        _settings.Meditation.Enabled = false;
        _world.Hp = 100;
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void StandsWhenBothRestoredToOwnThresholds()
    {
        _world.Hp = 200;
        Tick();
        _world.Sitting = true;

        _world.Hp = 460; // 92 % ≥ 90 %, MP 100 % ≥ 80 %
        _world.Mp = 300; // 60 % < 80 % — ещё сидим
        Tick();
        Assert.Equal(0, Count("stand"));

        _world.Mp = 410; // 82 %
        Tick();
        Assert.Equal(1, Count("stand"));
    }

    [Fact]
    public void AttackedWhileSittingStands()
    {
        _world.Hp = 200;
        Tick();
        _world.Sitting = true;
        Tick();

        _world.AddMob(0x80000001, "Волк", 5, targetWid: FakeWorld.HostWid);
        Tick();
        Assert.Equal(1, Count("stand"));
    }

    [Fact]
    public void AttackedDoesNotSit()
    {
        _world.Hp = 200;
        _world.AddMob(0x80000001, "Волк", 5, targetWid: FakeWorld.HostWid);
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void FlyingDoesNotSit()
    {
        _world.Hp = 200;
        _world.Flying = true;
        Tick();
        Assert.Equal(0, Count("sit"));
    }

    [Fact]
    public void NoCapabilityDoesNotSit()
    {
        _actions.Capabilities = Capabilities.All.Without(Capability.Meditate, "нет");
        _world.Hp = 200;
        Tick();
        Assert.Equal(0, Count("sit"));
    }
}

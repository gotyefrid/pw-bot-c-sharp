using BotCH.Core.World;
using BotCH.Tests.Fakes;
using Xunit;

namespace BotCH.Tests.World;

/// <summary>Расстояния до мобов и предметов — из позиций, от персонажа в этом снимке.</summary>
public class OffsetTests
{
    private readonly FakeWorld _world = new();

    [Fact]
    public void CountedFromHostInThisSnapshot()
    {
        _world.Position = new Position(5, 100, 0);
        _world.AddMob(1, "Волк", 8);
        _world.Ground.Add(new GroundItem(0, 2, 3044, GroundItemKind.Money, new Position(5, 103, 4), "Монета"));

        var w = _world.Snapshot();

        Assert.Equal(new Offset(3, -100), Assert.Single(w.Npcs).Offset);
        var coin = Assert.Single(w.GroundItems).Offset;
        Assert.Equal((4f, 3f, 5f), (coin.Horizontal, coin.Vertical, coin.Direct));
    }

    [Fact]
    public void WithPositionDoesNotKeepOldDistance()
    {
        _world.AddMob(1, "Волк", 10);
        var mob = Assert.Single(_world.Snapshot().Npcs);

        var moved = mob with { Position = new Position(20, 0, 0) };

        Assert.Equal(20f, moved.Offset.Horizontal);
    }

    [Theory]
    [InlineData(2f, "12,0 м")]
    [InlineData(30f, "12,0 м (выше 30 м)")]
    [InlineData(-113f, "12,0 м (ниже 113 м)")]
    public void HeightShownWhereNotable(float vertical, string text)
        => Assert.Equal(text, new Offset(12, vertical).ToString());
}

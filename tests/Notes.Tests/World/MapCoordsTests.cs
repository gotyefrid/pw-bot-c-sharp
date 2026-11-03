using Notes.Core.World;
using Xunit;

namespace Notes.Tests.World;

public class MapCoordsTests
{
    // Флай на PW Classic: снимок из памяти и миникарта в тот же момент
    [Fact]
    public void MatchesMinimapInGame()
    {
        var map = MapCoords.From(new Position(-61.9f, 224.9f, -1033.9f));

        Assert.Equal(394, map.X, 0);
        Assert.Equal(447, map.Y, 0);
        Assert.Equal(22, map.Height, 0);
    }

    [Fact]
    public void BackToMemoryCoordinates()
    {
        var p = new MapCoords(394, 447, 22).ToPosition();

        Assert.Equal(new Position(-60, 220, -1030), p);
    }
}

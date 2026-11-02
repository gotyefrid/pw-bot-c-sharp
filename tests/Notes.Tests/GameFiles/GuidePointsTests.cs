using System;
using System.IO;
using Notes.Core.GameFiles;
using Notes.Core.Profiles;
using Notes.Core.World;
using Xunit;

namespace Notes.Tests.GameFiles;

public class GuidePointsTests
{
    // Кусок configs\coord_data.txt Comeback 1.4.6: заголовок, подземелье, мотыль (две точки) и Старейшина Города Оборотней
    private const string CoordData = "ID  worldtag    x   y   z\r\n" +
                                     "a01\ta01\t366.54\t238.66\t-10.75\r\n" +
                                     "1000\tworld\t-1458.20\t247.06\t1325.14\r\n" +
                                     "1000\tworld\t-1537.19\t254.01\t825.00\r\n" +
                                     "1000\ta05\t1.00\t2.00\t3.00\r\n" +
                                     "2206\tworld\t-1537.85\t258.59\t969.71\r\n";

    // Тест на файлах настоящего клиента: папка element — в переменной окружения. Нет переменной — тест пропускается
    private const string ComebackVariable = "NOTES_COMEBACK146_DIR";
    private static readonly string ComebackDirectory = Environment.GetEnvironmentVariable(ComebackVariable) ?? "";

    private static GuidePoints Points() => new(GuidePoints.Parse(CoordData));

    [Fact]
    public void ReadsMainWorldLinesOnly()
    {
        var points = Points();

        Assert.Equal(2, points.Count);
        Assert.Equal(2, points.Of(1000).Count);
        Assert.Equal(new Position(-1537.85f, 258.59f, 969.71f), points.Of(2206)[0]);
    }

    [Fact]
    public void NearestPicksClosestPointOfKind()
    {
        var near = Points().Nearest(1000, new Position(-1530, 250, 900));

        Assert.Equal(new Position(-1537.19f, 254.01f, 825.00f), near);
    }

    [Fact]
    public void UnknownKindHasNoPoints()
    {
        Assert.Empty(Points().Of(3303));
        Assert.Null(Points().Nearest(3303, new Position(0, 0, 0)));
    }

    [Fact]
    public void RealClientKnowsQuestMobsAndNpcs()
    {
        Assert.SkipUnless(Directory.Exists(ComebackDirectory), $"Не задана {ComebackVariable} — папка element клиента Comeback 1.4.6");

        var points = GuidePoints.LoadFromGameDirectory(ComebackDirectory, new ProfileCatalog().Load("comeback146").Data.GameFiles, out var problem);

        Assert.Null(problem);
        // Малая росянка и Малахитовый мотыль — по многу точек; Наставница и Старейшина Города Оборотней — по одной
        Assert.True(points.Of(3303).Count > 5);
        Assert.True(points.Of(1000).Count > 5);
        Assert.Single(points.Of(3518));
        Assert.Single(points.Of(2206));
    }
}

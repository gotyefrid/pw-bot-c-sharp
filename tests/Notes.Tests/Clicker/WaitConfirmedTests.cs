using Notes.Core.Clicker;
using Xunit;

namespace Notes.Tests.Clicker;

/// <summary>
/// Галочка «Ждать подтверждения выполнения» — общая у действий, итог которых виден по игре (<see cref="ConfirmableAction"/>).
/// Как ждёт каждое действие — в его файле тестов.
/// </summary>
public class WaitConfirmedTests
{
    [Fact]
    public void OffByDefault() => Assert.False(new PickUpItemAction().WaitConfirmed);

    [Fact]
    public void TableShowsThatStepWaits()
    {
        Assert.Equal("поднять ближайший предмет", new PickUpItemAction().Describe());
        Assert.Equal("поднять ближайший предмет, жду подтверждения", new PickUpItemAction { WaitConfirmed = true }.Describe());
    }

    [Fact]
    public void FileHasItOnlyWhenOn()
    {
        Assert.DoesNotContain("\"waitConfirmed\"", ClickerScript.Json(new PickUpItemAction()));
        Assert.Contains("\"waitConfirmed\": true", ClickerScript.Json(new PickUpItemAction { WaitConfirmed = true }));
        Assert.True(ClickerScript.RoundTrip(new PickUpItemAction { WaitConfirmed = true }).WaitConfirmed);
    }

    [Fact]
    public void EditIsReported()
    {
        var action = new PickUpItemAction();
        var edits = 0;
        action.PropertyChanged += (_, _) => edits++;

        action.WaitConfirmed = true;
        action.WaitConfirmed = true;

        Assert.Equal(1, edits);
    }
}

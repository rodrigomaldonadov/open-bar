using OpenBar.Dock;
using OpenBar.Domain.Layouts;
using Xunit;

namespace OpenBar.Dock.Tests;

public sealed class DockAutoHideControllerTests
{
    private static readonly DockRect WorkArea = new(0, 0, 1920, 1040);
    private static readonly DockRect DockBounds = new(800, 940, 320, 72);
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DisabledControllerNeverHides()
    {
        var controller = new DockAutoHideController { Enabled = false };

        var action = controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(10, 10), Now);

        Assert.Equal(AutoHideAction.None, action);
        Assert.False(controller.IsHidden);
    }

    [Fact]
    public void HidesAfterTheDelayWhenThePointerLeaves()
    {
        var controller = new DockAutoHideController { Enabled = true, HideDelay = TimeSpan.FromMilliseconds(500) };

        Assert.Equal(AutoHideAction.None, controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(900, 980), Now));
        Assert.Equal(AutoHideAction.None, controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(100, 100), Now));
        Assert.Equal(AutoHideAction.None, controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(100, 100), Now.AddMilliseconds(300)));
        Assert.Equal(AutoHideAction.Hide, controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(100, 100), Now.AddMilliseconds(600)));
        Assert.True(controller.IsHidden);
    }

    [Fact]
    public void ReEnteringTheDockCancelsTheHideDelay()
    {
        var controller = new DockAutoHideController { Enabled = true, HideDelay = TimeSpan.FromMilliseconds(500) };

        _ = controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(900, 980), Now);
        _ = controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(100, 100), Now);
        _ = controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(900, 980), Now.AddMilliseconds(200));

        Assert.Equal(
            AutoHideAction.None,
            controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(100, 100), Now.AddMilliseconds(600)));
    }

    [Fact]
    public void MovingToTheActivationEdgeShowsAHiddenDock()
    {
        var controller = new DockAutoHideController { Enabled = true, HideDelay = TimeSpan.Zero, ShowDelay = TimeSpan.Zero };
        controller.Reset(hidden: true);

        var action = controller.Update(
            DockBounds,
            WorkArea,
            DockEdge.Bottom,
            new DockPoint(900, WorkArea.Bottom),
            Now);

        Assert.Equal(AutoHideAction.Show, action);
        Assert.False(controller.IsHidden);
    }

    [Fact]
    public void EdgeActivationWorksForEveryDockEdge()
    {
        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            var controller = new DockAutoHideController { Enabled = true, ShowDelay = TimeSpan.Zero };
            controller.Reset(hidden: true);

            var cursor = edge switch
            {
                DockEdge.Top => new DockPoint(900, WorkArea.Top),
                DockEdge.Bottom => new DockPoint(900, WorkArea.Bottom),
                DockEdge.Left => new DockPoint(WorkArea.Left, 500),
                _ => new DockPoint(WorkArea.Right, 500)
            };

            Assert.Equal(AutoHideAction.Show, controller.Update(DockBounds, WorkArea, edge, cursor, Now));
        }
    }

    [Fact]
    public void ActivationEdgeWaitsForTheConfiguredShowDelay()
    {
        var controller = new DockAutoHideController
        {
            Enabled = true,
            ShowDelay = TimeSpan.FromMilliseconds(250)
        };
        controller.Reset(hidden: true);
        var cursor = new DockPoint(900, WorkArea.Bottom);

        Assert.Equal(AutoHideAction.None, controller.Update(DockBounds, WorkArea, DockEdge.Bottom, cursor, Now));
        Assert.Equal(AutoHideAction.None, controller.Update(DockBounds, WorkArea, DockEdge.Bottom, cursor, Now.AddMilliseconds(100)));
        Assert.Equal(AutoHideAction.Show, controller.Update(DockBounds, WorkArea, DockEdge.Bottom, cursor, Now.AddMilliseconds(300)));
    }

    [Fact]
    public void LeavingTheEdgeBeforeTheShowDelayCancelsTheReveal()
    {
        var controller = new DockAutoHideController
        {
            Enabled = true,
            ShowDelay = TimeSpan.FromMilliseconds(250)
        };
        controller.Reset(hidden: true);

        _ = controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(900, WorkArea.Bottom), Now);
        _ = controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(900, 500), Now.AddMilliseconds(100));

        Assert.Equal(
            AutoHideAction.None,
            controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(900, WorkArea.Bottom), Now.AddMilliseconds(200)));
    }

    [Fact]
    public void DisablingAutoHideShowsAHiddenDock()
    {
        var controller = new DockAutoHideController { Enabled = true };
        controller.Reset(hidden: true);
        controller.Enabled = false;

        Assert.Equal(
            AutoHideAction.Show,
            controller.Update(DockBounds, WorkArea, DockEdge.Bottom, new DockPoint(500, 500), Now));
        Assert.False(controller.IsHidden);
    }
}

using OpenBar.Dock;
using OpenBar.Domain.Layouts;
using Xunit;

namespace OpenBar.Dock.Tests;

public sealed class DockLayoutCalculatorTests
{
    private static DockLayout Layout(
        DockEdge edge = DockEdge.Bottom,
        DockAlignment alignment = DockAlignment.Center)
    {
        var layout = DockLayoutDefaults.Create();
        layout.Position = edge;
        layout.Alignment = alignment;
        return layout;
    }

    [Fact]
    public void DefaultsIncludeTheVisualPolishFields()
    {
        var layout = DockLayoutDefaults.Create();

        Assert.Equal(DockTheme.System, layout.Theme);
        Assert.Equal(22, layout.CornerRadius);
        Assert.Equal(150, layout.AutoHideShowDelayMs);
        Assert.Equal(550, layout.AutoHideHideDelayMs);
        Assert.True(layout.HideInFullscreen);
        Assert.False(layout.ReduceAnimations);
    }

    [Fact]
    public void HorizontalDockSizeIncludesItemsSpacingAndAddTile()
    {
        var metrics = DockLayoutCalculator.Calculate(Layout(), pinnedCount: 2, runningCount: 0);

        Assert.Equal(4, metrics.ItemCount);
        Assert.Equal(4 * 48 + 3 * 10 + 24, metrics.Width);
        Assert.Equal(72, metrics.Height);
    }

    [Fact]
    public void VerticalDockSwapsLengthAndThickness()
    {
        var metrics = DockLayoutCalculator.Calculate(Layout(DockEdge.Left), pinnedCount: 2, runningCount: 0);

        Assert.Equal(72, metrics.Width);
        Assert.Equal(246, metrics.Height);
    }

    [Fact]
    public void RunningSectionAddsADividerGap()
    {
        var pinnedOnly = DockLayoutCalculator.Calculate(Layout(), 2, 0);
        var both = DockLayoutCalculator.Calculate(Layout(), 2, 2);

        Assert.Equal(pinnedOnly.Width + 2 * 48 + 2 * 10 + 21, both.Width);    }

    [Fact]
    public void BottomCenterHonorsOffset()
    {
        var bounds = DockLayoutCalculator.CalculateBounds(
            Layout(),
            new DockMetrics(246, 72, 4),
            new DockRect(0, 0, 1920, 1040),
            1.0);

        Assert.Equal(837, bounds.Left);
        Assert.Equal(956, bounds.Top);
        Assert.Equal(246, bounds.Width);
        Assert.Equal(72, bounds.Height);
    }

    [Fact]
    public void TopAndSideEdgesUseTheCorrectAnchor()
    {
        var top = DockLayoutCalculator.CalculateBounds(
            Layout(DockEdge.Top),
            new DockMetrics(246, 72, 4),
            new DockRect(0, 0, 1920, 1040),
            1.0);
        Assert.Equal(12, top.Top);

        var left = DockLayoutCalculator.CalculateBounds(
            Layout(DockEdge.Left),
            new DockMetrics(72, 246, 4),
            new DockRect(0, 0, 1920, 1040),
            1.0);
        Assert.Equal(12, left.Left);
        Assert.Equal(397, left.Top);

        var right = DockLayoutCalculator.CalculateBounds(
            Layout(DockEdge.Right),
            new DockMetrics(72, 246, 4),
            new DockRect(0, 0, 1920, 1040),
            1.0);
        Assert.Equal(1836, right.Left);
    }

    [Fact]
    public void DpiScaleIsAppliedToSizePositionAndOffset()
    {
        var bounds = DockLayoutCalculator.CalculateBounds(
            Layout(),
            new DockMetrics(246, 72, 4),
            new DockRect(0, 0, 1920, 1040),
            1.5);

        Assert.Equal(369, bounds.Width);
        Assert.Equal(108, bounds.Height);
        Assert.Equal(775, bounds.Left);
        Assert.Equal(914, bounds.Top);
    }

    [Fact]
    public void StretchUsesTheWorkAreaMinusOffset()
    {
        var bounds = DockLayoutCalculator.CalculateBounds(
            Layout(alignment: DockAlignment.Stretch),
            new DockMetrics(246, 72, 4),
            new DockRect(0, 0, 1920, 1040),
            1.0);

        Assert.Equal(1896, bounds.Width);
        Assert.Equal(12, bounds.Left);
    }

    [Fact]
    public void OversizedDockIsClampedToTheWorkArea()
    {
        var bounds = DockLayoutCalculator.CalculateBounds(
            Layout(),
            new DockMetrics(4_000, 700, 1),
            new DockRect(100, 50, 800, 600),
            1.0);

        Assert.Equal(800, bounds.Width);
        Assert.Equal(600, bounds.Height);
        Assert.Equal(100, bounds.Left);
        Assert.Equal(50, bounds.Top);
    }

    [Fact]
    public void MonitorResolverFallsBackToPrimaryWhenTheIndexIsMissing()
    {
        var monitors = new[]
        {
            new DockMonitorInfo("1", true, new DockRect(0, 0, 1920, 1080)),
            new DockMonitorInfo("2", false, new DockRect(1920, 0, 1280, 1024))
        };

        Assert.Equal("1", DockMonitorResolver.Select("primary", monitors)!.Id);
        Assert.Equal("2", DockMonitorResolver.Select("index:1", monitors)!.Id);
        Assert.Equal("1", DockMonitorResolver.Select("index:9", monitors)!.Id);
        Assert.Null(DockMonitorResolver.Select("primary", []));
    }
}

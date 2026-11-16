using OpenBar.Dock;
using Xunit;

namespace OpenBar.Dock.Tests;

public sealed class DockMagnificationCalculatorTests
{
    [Fact]
    public void CursorOverAnItemProducesTheMaximumScale()
    {
        var scales = DockMagnificationCalculator.Calculate([0, 50, 100, 150, 200], 100, 48, 10, 1.5);

        Assert.Equal(1.5, scales[2], 3);
    }

    [Fact]
    public void NeighboursReactProportionallyAndFarItemsStayAtOne()
    {
        var scales = DockMagnificationCalculator.Calculate([0, 50, 100, 150, 200], 100, 48, 10, 1.5);

        Assert.True(scales[1] > 1 && scales[1] < 1.5);
        Assert.True(scales[3] > 1 && scales[3] < 1.5);
        Assert.Equal(scales[1], scales[3], 3);
        Assert.Equal(1, scales[0]);
        Assert.Equal(1, scales[4]);
    }

    [Fact]
    public void ScaleDecreasesWithDistance()
    {
        var scales = DockMagnificationCalculator.Calculate([0, 30, 60, 90], 0, 48, 10, 1.6);

        Assert.True(scales[0] > scales[1]);
        Assert.True(scales[1] > scales[2]);
    }

    [Fact]
    public void DisabledMagnificationReturnsUnitScales()
    {
        var scales = DockMagnificationCalculator.Calculate([0, 50, 100], 50, 48, 10, 1);

        Assert.All(scales, scale => Assert.Equal(1, scale));
    }

    [Fact]
    public void EmptyItemListIsHandled()
    {
        Assert.Empty(DockMagnificationCalculator.Calculate([], 0, 48, 10, 1.5));
    }
}

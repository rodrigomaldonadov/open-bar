using OpenBar.Domain.Layouts;

namespace OpenBar.Dock;

/// <summary>Integer rectangle in physical pixels.</summary>
public readonly record struct DockRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}

/// <summary>Integer point in physical pixels.</summary>
public readonly record struct DockPoint(int X, int Y);

/// <summary>Size of the dock bar in device-independent pixels.</summary>
public readonly record struct DockMetrics(double Width, double Height, int ItemCount, bool Overflow = false);

/// <summary>Length/thickness required by one embedded widget surface.</summary>
public readonly record struct DockSurfaceMetrics(double Length, double Thickness);

/// <summary>Pure geometry for the dock; no WinUI or Win32 dependency.</summary>
public static class DockLayoutCalculator
{
    public const double Padding = 12;

    public static DockMetrics Calculate(
        DockLayout layout,
        int pinnedCount,
        int runningCount,
        bool includeAddTile = true,
        bool includeSystemStatus = true,
        IReadOnlyList<DockSurfaceMetrics>? surfaces = null,
        double maxLength = 0,
        bool includeOverflow = false)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var iconSize = Math.Clamp(layout.IconSize, 24, 96);
        var spacing = Math.Clamp(layout.Spacing, 0, 40);
        var surfaceList = surfaces ?? [];

        var itemCount = Math.Max(0, pinnedCount) + Math.Max(0, runningCount) + (includeAddTile ? 2 : 0);
        itemCount += includeSystemStatus ? 1 : 0;
        itemCount += surfaceList.Count;
        itemCount += includeOverflow ? 1 : 0;
        itemCount = Math.Max(1, itemCount);

        var length = itemCount * iconSize + Math.Max(0, itemCount - 1) * spacing + 2 * Padding;

        if (pinnedCount > 0 && runningCount > 0)
        {
            // Divider plus breathing room between the pinned and running sections.
            length += spacing * 2 + 1;
        }

        if (surfaceList.Count > 0)
        {
            // Embedded surfaces have independent lengths; add their extra length
            // on top of the fixed icon cells plus one spacing slot each.
            length += surfaceList.Sum(surface => Math.Max(0, surface.Length - iconSize) + spacing);
        }

        var thickness = iconSize + 2 * Padding;
        if (surfaceList.Count > 0)
        {
            var widestSurface = surfaceList.Max(surface => surface.Thickness);
            thickness = Math.Max(thickness, widestSurface + 2 * Padding);
        }

        var overflow = false;
        if (maxLength > 0 && length > maxLength)
        {
            length = maxLength;
            overflow = true;
        }

        return layout.Position is DockEdge.Top or DockEdge.Bottom
            ? new DockMetrics(length, thickness, itemCount, overflow)
            : new DockMetrics(thickness, length, itemCount, overflow);
    }

    public static DockRect CalculateBounds(
        DockLayout layout,
        DockMetrics metrics,
        DockRect workArea,
        double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (!double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            dpiScale = 1;
        }

        var offset = Math.Max(0, layout.Offset) * dpiScale;
        var width = (int)Math.Round(metrics.Width * dpiScale);
        var height = (int)Math.Round(metrics.Height * dpiScale);

        if (layout.Alignment == DockAlignment.Stretch)
        {
            if (layout.Position is DockEdge.Top or DockEdge.Bottom)
            {
                width = Math.Max(1, workArea.Width - (int)Math.Round(offset * 2));
            }
            else
            {
                height = Math.Max(1, workArea.Height - (int)Math.Round(offset * 2));
            }
        }

        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);

        var left = layout.Position switch
        {
            DockEdge.Left => workArea.Left + (int)Math.Round(offset),
            DockEdge.Right => workArea.Right - width - (int)Math.Round(offset),
            _ => Align(workArea.Left, workArea.Width, width, layout.Alignment, offset)
        };

        var top = layout.Position switch
        {
            DockEdge.Top => workArea.Top + (int)Math.Round(offset),
            DockEdge.Bottom => workArea.Bottom - height - (int)Math.Round(offset),
            _ => Align(workArea.Top, workArea.Height, height, layout.Alignment, offset)
        };

        left = Math.Clamp(left, workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));

        return new DockRect(left, top, width, height);
    }

    private static int Align(
        int start,
        int available,
        int size,
        DockAlignment alignment,
        double offset)
    {
        return alignment switch
        {
            DockAlignment.Start => start + (int)Math.Round(offset),
            DockAlignment.End => start + available - size - (int)Math.Round(offset),
            DockAlignment.Stretch => start + (int)Math.Round(offset),
            _ => start + (available - size) / 2
        };
    }
}

using OpenBar.Domain.Layouts;

namespace OpenBar.Dock;

/// <summary>Default dock layout created on first run.</summary>
public static class DockLayoutDefaults
{
    public static DockLayout Create()
        => new()
        {
            Name = "Default",
            Position = DockEdge.Bottom,
            Alignment = DockAlignment.Center,
            Size = 72,
            Offset = 12,
            IconSize = 48,
            Spacing = 10,
            Magnification = 1.12,
            MaterialOpacity = 0.55,
            AnimationDurationMs = 180,
            CornerRadius = 22,
            Theme = DockTheme.System,
            AutoHideShowDelayMs = 150,
            AutoHideHideDelayMs = 550,
            ReduceAnimations = false,
            HideInFullscreen = true,
            AutoHide = false,
            IsDefault = true,
            MonitorTarget = DockLayout.PrimaryMonitorTarget
        };
}

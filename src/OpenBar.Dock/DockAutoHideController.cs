using OpenBar.Domain.Layouts;

namespace OpenBar.Dock;

public enum AutoHideAction
{
    None,
    Show,
    Hide
}

/// <summary>
/// Deterministic auto-hide state machine. The host feeds cursor positions on a
/// low-frequency timer; the controller owns the delay so showing/hiding cannot
/// flicker or depend on multiple ad-hoc timers.
/// </summary>
public sealed class DockAutoHideController
{
    private DateTimeOffset? _outsideSince;

    public bool Enabled { get; set; }

    public TimeSpan HideDelay { get; set; } = TimeSpan.FromMilliseconds(550);

    public int ActivationZoneThickness { get; set; } = 4;

    public bool IsHidden { get; private set; }

    public AutoHideAction Update(
        DockRect dockBounds,
        DockRect workArea,
        DockEdge edge,
        DockPoint cursor,
        DateTimeOffset now)
    {
        if (!Enabled)
        {
            _outsideSince = null;

            if (IsHidden)
            {
                IsHidden = false;
                return AutoHideAction.Show;
            }

            return AutoHideAction.None;
        }

        if (IsHidden)
        {
            if (IsAtActivationEdge(workArea, edge, cursor))
            {
                IsHidden = false;
                _outsideSince = null;
                return AutoHideAction.Show;
            }

            return AutoHideAction.None;
        }

        if (IsInside(dockBounds, cursor, 8))
        {
            _outsideSince = null;
            return AutoHideAction.None;
        }

        _outsideSince ??= now;

        if (now - _outsideSince.Value < HideDelay)
        {
            return AutoHideAction.None;
        }

        IsHidden = true;
        _outsideSince = null;
        return AutoHideAction.Hide;
    }

    public void Reset(bool hidden)
    {
        IsHidden = hidden;
        _outsideSince = null;
    }

    private bool IsAtActivationEdge(DockRect workArea, DockEdge edge, DockPoint cursor)
    {
        var zone = Math.Max(1, ActivationZoneThickness);

        return edge switch
        {
            DockEdge.Bottom => cursor.Y >= workArea.Bottom - zone,
            DockEdge.Top => cursor.Y <= workArea.Top + zone,
            DockEdge.Left => cursor.X <= workArea.Left + zone,
            DockEdge.Right => cursor.X >= workArea.Right - zone,
            _ => false
        };
    }

    private static bool IsInside(DockRect bounds, DockPoint point, int margin)
        => point.X >= bounds.Left - margin
           && point.X <= bounds.Right + margin
           && point.Y >= bounds.Top - margin
           && point.Y <= bounds.Bottom + margin;
}

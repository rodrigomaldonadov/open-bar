namespace OpenBar.Domain.Layouts;

/// <summary>
/// A persisted dock layout. Monitor targeting deliberately avoids hardware
/// identifiers: <see cref="MonitorTarget"/> stores a logical selector such as
/// <c>primary</c> or <c>index:1</c>, which can be re-resolved at runtime when
/// monitors are added, removed or rearranged.
/// </summary>
public sealed class DockLayout
{
    public const int MaxNameLength = 200;
    public const int MaxMonitorTargetLength = 100;
    public const string PrimaryMonitorTarget = "primary";

    public Guid Id { get; set; }

    /// <summary>User-visible layout name (unique).</summary>
    public string Name { get; set; } = string.Empty;

    public DockEdge Position { get; set; } = DockEdge.Bottom;

    public DockAlignment Alignment { get; set; } = DockAlignment.Center;

    /// <summary>Dock thickness along the attached edge, in device-independent pixels.</summary>
    public double Size { get; set; } = 72;

    /// <summary>Distance from the screen edge, in device-independent pixels.</summary>
    public double Offset { get; set; }

    /// <summary>Icon size in device-independent pixels.</summary>
    public double IconSize { get; set; } = 48;

    /// <summary>Gap between dock items in device-independent pixels.</summary>
    public double Spacing { get; set; } = 10;

    /// <summary>Hover magnification factor (1.0 = no magnification).</summary>
    public double Magnification { get; set; } = 1.12;

    /// <summary>Opacity of the glass tint overlay (0..1).</summary>
    public double MaterialOpacity { get; set; } = 0.55;

    /// <summary>Animation duration in milliseconds.</summary>
    public int AnimationDurationMs { get; set; } = 180;

    public bool AutoHide { get; set; }

    /// <summary>At most one layout is the default (enforced by a filtered unique index).</summary>
    public bool IsDefault { get; set; }

    /// <summary>Logical monitor selector: <c>primary</c> or <c>index:N</c>.</summary>
    public string MonitorTarget { get; set; } = PrimaryMonitorTarget;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

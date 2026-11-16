namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Presentation and management metadata for a widget type. The metadata is
/// owned by the widget module and is copied into the registry at discovery
/// time; it never depends on persisted user instances.
/// </summary>
public sealed record WidgetMetadata
{
    public const int MaxDisplayNameLength = 200;
    public const int MaxCategoryLength = 100;
    public const int MaxVersionLength = 50;
    public const int MaxAuthorLength = 200;
    public const int MaxDescriptionLength = 2_000;

    /// <summary>User-visible name (for example "Reloj").</summary>
    public required string DisplayName { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>Catalog category (for example "System").</summary>
    public required string Category { get; init; }

    /// <summary>Module version, not the settings schema version.</summary>
    public required string Version { get; init; }

    public string? Author { get; init; }

    public double MinWidth { get; init; } = 160;

    public double MinHeight { get; init; } = 80;

    public double MaxWidth { get; init; } = 800;

    public double MaxHeight { get; init; } = 600;

    /// <summary>Initial width used when a new instance has no persisted geometry.</summary>
    public double DefaultWidth { get; init; } = 280;

    /// <summary>Initial height used when a new instance has no persisted geometry.</summary>
    public double DefaultHeight { get; init; } = 160;

    /// <summary>Preferred dock length when the dock is horizontal, in DIPs.</summary>
    public double CompactWidth { get; init; } = 160;

    /// <summary>Preferred dock length when the dock is vertical, in DIPs.</summary>
    public double CompactHeight { get; init; } = 96;

    public WidgetCapabilities Capabilities { get; init; } = WidgetCapabilities.None;

    /// <summary>When false, the runtime rejects a second persisted instance.</summary>
    public bool AllowsMultipleInstances { get; init; } = true;
}

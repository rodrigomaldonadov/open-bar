namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Definition of a widget type. <see cref="WidgetId"/> is a stable logical id
/// chosen by the module; it must not derive from a display name or a physical
/// assembly name. The factory creates a fresh widget object for every
/// lifecycle start so a stopped instance holds no runtime resources.
/// </summary>
public sealed record WidgetDefinition
{
    public const int MaxWidgetIdLength = 100;

    /// <summary>
    /// Stable, lower-case identifier (for example <c>openbar.clock</c>).
    /// </summary>
    public required string WidgetId { get; init; }

    public required WidgetMetadata Metadata { get; init; }

    /// <summary>Creates a new widget object. Must not return null.</summary>
    public required Func<IWidget> CreateWidget { get; init; }

    /// <summary>Current JSON configuration schema version for new instances.</summary>
    public int SettingsSchemaVersion { get; init; } = 1;

    /// <summary>JSON written when a new instance is created without explicit settings.</summary>
    public string DefaultSettingsJson { get; init; } = "{}";
}

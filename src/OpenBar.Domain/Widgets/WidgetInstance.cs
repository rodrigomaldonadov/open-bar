namespace OpenBar.Domain.Widgets;

/// <summary>
/// A persisted instance of a widget type. Several instances may exist for the
/// same <see cref="WidgetId"/> as long as their <see cref="InstanceName"/> is
/// unique within that widget type.
/// </summary>
public sealed class WidgetInstance
{
    public const int MaxWidgetIdLength = 100;
    public const int MaxInstanceNameLength = 200;
    public const int DefaultSettingsSchemaVersion = 1;

    public Guid Id { get; set; }

    /// <summary>Logical widget type identifier (for example "clock").</summary>
    public string WidgetId { get; set; } = string.Empty;

    /// <summary>User-visible name, unique per widget type.</summary>
    public string InstanceName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    /// <summary>Horizontal position in device-independent pixels, if placed.</summary>
    public double? PositionX { get; set; }

    /// <summary>Vertical position in device-independent pixels, if placed.</summary>
    public double? PositionY { get; set; }

    public double? Width { get; set; }

    public double? Height { get; set; }

    /// <summary>Widget-specific JSON configuration. Must be valid JSON.</summary>
    public string SettingsJson { get; set; } = "{}";

    public int SettingsSchemaVersion { get; set; } = DefaultSettingsSchemaVersion;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

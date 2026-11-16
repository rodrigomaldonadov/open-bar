using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.Clock;

/// <summary>Definition and default configuration for the official clock widget.</summary>
public static class ClockWidgetDefinition
{
    public const string WidgetId = "openbar.clock";

    public const string DefaultSettingsJson =
        """{"use24Hour":true,"showSeconds":true,"showDate":true}""";

    public static WidgetDefinition Create()
        => new()
        {
            WidgetId = WidgetId,
            SettingsSchemaVersion = 1,
            DefaultSettingsJson = DefaultSettingsJson,
            Metadata = new WidgetMetadata
            {
                DisplayName = "Reloj",
                Description = "Hora local con actualización periódica.",
                Category = "System",
                Version = "1.0.0",
                Author = "OpenBar",
                MinWidth = 180,
                MinHeight = 100,
                MaxWidth = 640,
                MaxHeight = 360,
                DefaultWidth = 260,
                DefaultHeight = 150,
                CompactWidth = 160,
                CompactHeight = 96,
                Capabilities = WidgetCapabilities.Settings | WidgetCapabilities.PeriodicUpdates,
                AllowsMultipleInstances = true
            },
            CreateWidget = static () => new ClockWidget()
        };
}

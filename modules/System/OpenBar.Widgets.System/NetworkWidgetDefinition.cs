using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public static class NetworkWidgetDefinition
{
    public const string WidgetId = "openbar.network";

    public const string DefaultSettingsJson =
        """{"intervalSeconds":2,"adapterId":""}""";

    public static WidgetDefinition Create()
        => new()
        {
            WidgetId = WidgetId,
            SettingsSchemaVersion = 1,
            DefaultSettingsJson = DefaultSettingsJson,
            Metadata = new WidgetMetadata
            {
                DisplayName = "Red",
                Description = "Conectividad y velocidad de descarga/subida.",
                Category = "System",
                Version = "1.0.0",
                Author = "OpenBar",
                MinWidth = 180,
                MinHeight = 100,
                MaxWidth = 560,
                MaxHeight = 320,
                DefaultWidth = 280,
                DefaultHeight = 140,
                CompactWidth = 190,
                CompactHeight = 96,
                Capabilities = WidgetCapabilities.Settings | WidgetCapabilities.PeriodicUpdates,
                AllowsMultipleInstances = true
            },
            CreateWidget = static () => new NetworkWidget()
        };
}

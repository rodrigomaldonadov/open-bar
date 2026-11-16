using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public static class BatteryWidgetDefinition
{
    public const string WidgetId = "openbar.battery";

    public const string DefaultSettingsJson = """{"intervalSeconds":5}""";

    public static WidgetDefinition Create()
        => new()
        {
            WidgetId = WidgetId,
            SettingsSchemaVersion = 1,
            DefaultSettingsJson = DefaultSettingsJson,
            Metadata = new WidgetMetadata
            {
                DisplayName = "Batería",
                Description = "Nivel de batería y estado de carga.",
                Category = "System",
                Version = "1.0.0",
                Author = "OpenBar",
                MinWidth = 180,
                MinHeight = 100,
                MaxWidth = 500,
                MaxHeight = 320,
                DefaultWidth = 240,
                DefaultHeight = 140,
                CompactWidth = 140,
                CompactHeight = 96,
                Capabilities = WidgetCapabilities.Settings | WidgetCapabilities.PeriodicUpdates,
                AllowsMultipleInstances = true
            },
            CreateWidget = static () => new BatteryWidget()
        };
}

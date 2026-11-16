using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public static class CpuWidgetDefinition
{
    public const string WidgetId = "openbar.cpu";

    public const string DefaultSettingsJson = """{"intervalSeconds":1}""";

    public static WidgetDefinition Create()
        => new()
        {
            WidgetId = WidgetId,
            SettingsSchemaVersion = 1,
            DefaultSettingsJson = DefaultSettingsJson,
            Metadata = new WidgetMetadata
            {
                DisplayName = "CPU",
                Description = "Uso total del procesador con actualización periódica.",
                Category = "System",
                Version = "1.0.0",
                Author = "OpenBar",
                MinWidth = 180,
                MinHeight = 100,
                MaxWidth = 520,
                MaxHeight = 320,
                DefaultWidth = 240,
                DefaultHeight = 140,
                CompactWidth = 150,
                CompactHeight = 96,
                Capabilities = WidgetCapabilities.Settings | WidgetCapabilities.PeriodicUpdates,
                AllowsMultipleInstances = true
            },
            CreateWidget = static () => new CpuWidget()
        };
}

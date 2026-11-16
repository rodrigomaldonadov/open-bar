using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public static class MemoryWidgetDefinition
{
    public const string WidgetId = "openbar.memory";

    public const string DefaultSettingsJson = """{"intervalSeconds":2,"unit":"auto"}""";

    public static WidgetDefinition Create()
        => new()
        {
            WidgetId = WidgetId,
            SettingsSchemaVersion = 1,
            DefaultSettingsJson = DefaultSettingsJson,
            Metadata = new WidgetMetadata
            {
                DisplayName = "Memoria RAM",
                Description = "Memoria física usada, total y porcentaje de utilización.",
                Category = "System",
                Version = "1.0.0",
                Author = "OpenBar",
                MinWidth = 180,
                MinHeight = 100,
                MaxWidth = 540,
                MaxHeight = 320,
                DefaultWidth = 260,
                DefaultHeight = 140,
                CompactWidth = 170,
                CompactHeight = 96,
                Capabilities = WidgetCapabilities.Settings | WidgetCapabilities.PeriodicUpdates,
                AllowsMultipleInstances = true
            },
            CreateWidget = static () => new MemoryWidget()
        };
}

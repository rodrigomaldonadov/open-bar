using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

public static class StorageWidgetDefinition
{
    public const string WidgetId = "openbar.storage";

    public const string DefaultSettingsJson =
        """{"intervalSeconds":5,"volumeId":""}""";

    public static WidgetDefinition Create()
        => new()
        {
            WidgetId = WidgetId,
            SettingsSchemaVersion = 1,
            DefaultSettingsJson = DefaultSettingsJson,
            Metadata = new WidgetMetadata
            {
                DisplayName = "Almacenamiento",
                Description = "Uso de un volumen local seleccionado.",
                Category = "System",
                Version = "1.0.0",
                Author = "OpenBar",
                MinWidth = 180,
                MinHeight = 100,
                MaxWidth = 560,
                MaxHeight = 320,
                DefaultWidth = 260,
                DefaultHeight = 140,
                CompactWidth = 175,
                CompactHeight = 96,
                Capabilities = WidgetCapabilities.Settings | WidgetCapabilities.PeriodicUpdates,
                AllowsMultipleInstances = true
            },
            CreateWidget = static () => new StorageWidget()
        };
}

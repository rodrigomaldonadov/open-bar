using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime.Tests;

internal static class TestDefinitions
{
    public const string WidgetId = "test.recording";

    public static WidgetDefinition Create(
        string widgetId = WidgetId,
        Func<IWidget>? factory = null,
        bool allowsMultipleInstances = true,
        int schemaVersion = 1,
        string settingsJson = "{}")
        => new()
        {
            WidgetId = widgetId,
            SettingsSchemaVersion = schemaVersion,
            DefaultSettingsJson = settingsJson,
            Metadata = new WidgetMetadata
            {
                DisplayName = "Test widget",
                Description = "Recording test widget.",
                Category = "Tests",
                Version = "1.0.0",
                Author = "OpenBar",
                MinWidth = 100,
                MinHeight = 60,
                MaxWidth = 800,
                MaxHeight = 600,
                DefaultWidth = 260,
                DefaultHeight = 150,
                Capabilities = WidgetCapabilities.Settings | WidgetCapabilities.PeriodicUpdates,
                AllowsMultipleInstances = allowsMultipleInstances
            },
            CreateWidget = factory ?? (static () => new Fakes.RecordingWidget())
        };
}

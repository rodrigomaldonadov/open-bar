using OpenBar.Widgets.Abstractions;
using Xunit;

namespace OpenBar.Domain.Tests;

public sealed class WidgetIdentifierTests
{
    private sealed class TestWidget : WidgetBase
    {
        public override object CreateView() => new object();
    }

    [Fact]
    public void WidgetDefinitionExposesStableMetadataAndFactory()
    {
        var definition = new WidgetDefinition
        {
            WidgetId = "openbar.clock",
            Metadata = new WidgetMetadata
            {
                DisplayName = "Reloj",
                Category = "System",
                Version = "1.0.0"
            },
            CreateWidget = static () => new TestWidget()
        };

        Assert.Equal("openbar.clock", definition.WidgetId);
        Assert.Equal("Reloj", definition.Metadata.DisplayName);
        Assert.Equal("System", definition.Metadata.Category);
        Assert.True(definition.Metadata.AllowsMultipleInstances);
        Assert.IsAssignableFrom<IWidget>(definition.CreateWidget());
    }

    [Fact]
    public void WidgetSettingsParsesAndDeserializesJson()
    {
        var settings = WidgetSettings.Parse("""{"use24Hour":false}""", 2);

        Assert.Equal(2, settings.SchemaVersion);
        Assert.Equal("""{"use24Hour":false}""", settings.Json);
        Assert.False(settings.Root.GetProperty("use24Hour").GetBoolean());
    }
}

using OpenBar.WidgetRuntime;
using OpenBar.Widgets.Abstractions;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class WidgetRegistryTests
{
    [Fact]
    public void RegisterAndFindReturnsTheDefinition()
    {
        var registry = new WidgetRegistry();
        var definition = TestDefinitions.Create();

        registry.Register(definition);

        Assert.Same(definition, registry.Find(TestDefinitions.WidgetId));
        Assert.Null(registry.Find("missing"));
        Assert.Single(registry.GetAll());
    }

    [Fact]
    public void DuplicateWidgetIdIsRejected()
    {
        var registry = new WidgetRegistry();
        registry.Register(TestDefinitions.Create());

        var ex = Assert.Throws<WidgetRegistrationException>(
            () => registry.Register(TestDefinitions.Create()));

        Assert.Equal(TestDefinitions.WidgetId, ex.WidgetId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Upper.Case")]
    [InlineData("with space")]
    [InlineData("bad/id")]
    public void InvalidWidgetIdsAreRejected(string widgetId)
    {
        var registry = new WidgetRegistry();

        Assert.Throws<WidgetDefinitionValidationException>(
            () => registry.Register(TestDefinitions.Create(widgetId)));
    }

    [Fact]
    public void InvalidMetadataIsRejected()
    {
        var registry = new WidgetRegistry();
        var definition = TestDefinitions.Create();
        var invalid = definition with
        {
            Metadata = definition.Metadata with
            {
                DisplayName = string.Empty
            }
        };

        var ex = Assert.Throws<WidgetDefinitionValidationException>(() => registry.Register(invalid));
        Assert.Equal("DisplayName", ex.PropertyName);
    }

    [Fact]
    public void InvalidDimensionBoundsAreRejected()
    {
        var registry = new WidgetRegistry();
        var definition = TestDefinitions.Create();
        var invalid = definition with
        {
            Metadata = definition.Metadata with
            {
                MinWidth = 500,
                MaxWidth = 100
            }
        };

        Assert.Throws<WidgetDefinitionValidationException>(() => registry.Register(invalid));
    }

    [Fact]
    public void DefaultDimensionsMustBeInsideTheBounds()
    {
        var registry = new WidgetRegistry();
        var definition = TestDefinitions.Create();
        var invalid = definition with
        {
            Metadata = definition.Metadata with
            {
                DefaultWidth = 1_000
            }
        };

        Assert.Throws<WidgetDefinitionValidationException>(() => registry.Register(invalid));
    }

    [Fact]
    public void UndefinedCapabilityFlagIsRejected()
    {
        var registry = new WidgetRegistry();
        var definition = TestDefinitions.Create();
        var invalid = definition with
        {
            Metadata = definition.Metadata with
            {
                Capabilities = (WidgetCapabilities)1_024
            }
        };

        Assert.Throws<WidgetDefinitionValidationException>(() => registry.Register(invalid));
    }

    [Fact]
    public void InvalidDefaultJsonIsRejected()
    {
        var registry = new WidgetRegistry();

        Assert.Throws<WidgetDefinitionValidationException>(
            () => registry.Register(TestDefinitions.Create(settingsJson: "{ not json")));
    }

    [Fact]
    public void NullFactoryIsRejected()
    {
        var registry = new WidgetRegistry();
        var definition = TestDefinitions.Create();
        var invalid = definition with { CreateWidget = null! };

        Assert.Throws<WidgetDefinitionValidationException>(() => registry.Register(invalid));
    }

    [Fact]
    public void GetAllIsSortedByWidgetId()
    {
        var registry = new WidgetRegistry();
        registry.Register(TestDefinitions.Create("z.widget"));
        registry.Register(TestDefinitions.Create("a.widget"));

        Assert.Equal(["a.widget", "z.widget"], registry.GetAll().Select(definition => definition.WidgetId));
    }
}

using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>Provider backed by definitions registered in the DI container.</summary>
public sealed class ConfiguredWidgetDefinitionProvider : IWidgetDefinitionProvider
{
    private readonly IReadOnlyList<WidgetDefinition> _definitions;

    public ConfiguredWidgetDefinitionProvider(IEnumerable<WidgetDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.ToArray();
    }

    public ValueTask<IReadOnlyList<WidgetDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default)
        => new(_definitions);
}

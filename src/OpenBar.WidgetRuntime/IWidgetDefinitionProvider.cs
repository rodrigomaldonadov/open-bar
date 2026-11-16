using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Supplies widget definitions. Official widgets are registered explicitly in
/// the composition root; a future plugin host can add another provider without
/// changing the runtime.
/// </summary>
public interface IWidgetDefinitionProvider
{
    ValueTask<IReadOnlyList<WidgetDefinition>> GetDefinitionsAsync(CancellationToken cancellationToken = default);
}

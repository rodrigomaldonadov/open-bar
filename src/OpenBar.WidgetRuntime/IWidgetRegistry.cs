using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// In-memory catalog of widget definitions. This is intentionally separate
/// from persisted user instances: a definition may exist with zero instances,
/// and an instance may survive while its definition is unavailable.
/// </summary>
public interface IWidgetRegistry
{
    void Register(WidgetDefinition definition);

    WidgetDefinition? Find(string widgetId);

    IReadOnlyList<WidgetDefinition> GetAll();
}

using System.Collections.Concurrent;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <inheritdoc cref="IWidgetRegistry" />
public sealed class WidgetRegistry : IWidgetRegistry
{
    private readonly ConcurrentDictionary<string, WidgetDefinition> _definitions =
        new(StringComparer.Ordinal);

    public void Register(WidgetDefinition definition)
    {
        WidgetDefinitionValidator.Validate(definition);

        if (!_definitions.TryAdd(definition.WidgetId, definition))
        {
            throw new WidgetRegistrationException(
                definition.WidgetId,
                $"A widget with id '{definition.WidgetId}' is already registered.");
        }
    }

    public WidgetDefinition? Find(string widgetId)
    {
        if (string.IsNullOrWhiteSpace(widgetId))
        {
            return null;
        }

        return _definitions.TryGetValue(widgetId, out var definition) ? definition : null;
    }

    public IReadOnlyList<WidgetDefinition> GetAll()
        => _definitions.Values
            .OrderBy(definition => definition.WidgetId, StringComparer.Ordinal)
            .ToArray();
}

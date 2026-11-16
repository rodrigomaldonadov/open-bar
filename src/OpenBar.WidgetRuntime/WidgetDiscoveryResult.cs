using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>Describes one definition that could not be registered.</summary>
public sealed record WidgetDiscoveryFailure(string Source, string? WidgetId, string Message);

/// <summary>Outcome of a discovery pass. Failures never abort the pass.</summary>
public sealed record WidgetDiscoveryResult(
    IReadOnlyList<WidgetDefinition> Registered,
    IReadOnlyList<WidgetDiscoveryFailure> Failures);

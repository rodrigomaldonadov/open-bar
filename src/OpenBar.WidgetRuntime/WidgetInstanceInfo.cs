namespace OpenBar.WidgetRuntime;

/// <summary>Immutable status snapshot for one persisted instance.</summary>
public sealed record WidgetInstanceInfo(
    Guid Id,
    string WidgetId,
    string InstanceName,
    WidgetInstanceState State,
    bool IsEnabled,
    bool IsAvailable,
    string? LastError,
    double? PositionX,
    double? PositionY,
    double? Width,
    double? Height,
    OpenBar.Domain.Widgets.WidgetPresentationMode PresentationMode
        = OpenBar.Domain.Widgets.WidgetPresentationMode.FloatingWindow,
    int PresentationOrder = 0);

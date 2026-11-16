using OpenBar.Widgets.Abstractions;

namespace OpenBar.Application.Widgets;

/// <summary>
/// Describes one widget window. The view itself is created by the host on the
/// UI thread by calling <see cref="IWidget.CreateView"/>; the runtime never
/// references XAML or window types.</summary>
public sealed record WidgetWindowRequest
{
    public required Guid InstanceId { get; init; }

    public required string WidgetId { get; init; }

    public required string Title { get; init; }

    public required IWidget Widget { get; init; }

    public required double Width { get; init; }

    public required double Height { get; init; }

    public double? PositionX { get; init; }

    public double? PositionY { get; init; }
}

public sealed class WidgetWindowCloseRequestedEventArgs : EventArgs
{
    public WidgetWindowCloseRequestedEventArgs(Guid instanceId)
    {
        InstanceId = instanceId;
    }

    public Guid InstanceId { get; }
}

/// <summary>
/// Host port for creating and closing independent widget windows. Implemented
/// by the WinUI app; the widget runtime depends only on this abstraction.
/// </summary>
public interface IWidgetWindowService
{
    /// <summary>Raised when the user closes a widget window.</summary>
    event EventHandler<WidgetWindowCloseRequestedEventArgs>? CloseRequested;

    Task OpenAsync(WidgetWindowRequest request, CancellationToken cancellationToken = default);

    Task CloseAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task CloseAllAsync(CancellationToken cancellationToken = default);
}

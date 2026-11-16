using OpenBar.Widgets.Abstractions;

namespace OpenBar.Application.Widgets;

/// <summary>Describes one compact surface hosted inside the dock.</summary>
public sealed record WidgetSurfaceRequest
{
    public required Guid InstanceId { get; init; }

    public required string WidgetId { get; init; }

    public required string Title { get; init; }

    public required IWidget Widget { get; init; }

    /// <summary>Preferred width when the dock is horizontal, in DIPs.</summary>
    public double CompactWidth { get; init; } = 160;

    /// <summary>Preferred height when the dock is vertical, in DIPs.</summary>
    public double CompactHeight { get; init; } = 96;

    public int Order { get; init; }
}

/// <summary>
/// Host port for embedded widget surfaces. Implemented by the dock; the widget
/// runtime depends only on this abstraction and never references XAML.
/// </summary>
public interface IWidgetSurfaceService
{
    Task OpenAsync(WidgetSurfaceRequest request, CancellationToken cancellationToken = default);

    Task CloseAsync(Guid instanceId, CancellationToken cancellationToken = default);

    Task UpdateOrderAsync(Guid instanceId, int order, CancellationToken cancellationToken = default);

    Task CloseAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>No-op surface host used by isolated tests and non-UI hosts.</summary>
public sealed class NullWidgetSurfaceService : IWidgetSurfaceService
{
    public static NullWidgetSurfaceService Instance { get; } = new();

    public Task OpenAsync(WidgetSurfaceRequest request, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CloseAsync(Guid instanceId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task UpdateOrderAsync(Guid instanceId, int order, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task CloseAllAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

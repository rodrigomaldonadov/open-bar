using OpenBar.Application.Widgets;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

internal sealed class FakeWidgetSurfaceService : IWidgetSurfaceService
{
    private readonly Dictionary<Guid, WidgetSurfaceRequest> _surfaces = new();

    public IReadOnlyCollection<WidgetSurfaceRequest> OpenSurfaces => _surfaces.Values.ToArray();

    public int CloseAllCalls { get; private set; }

    public bool ThrowOnOpen { get; set; }

    public Task OpenAsync(WidgetSurfaceRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnOpen)
        {
            throw new InvalidOperationException("Surface host failed.");
        }

        _surfaces[request.InstanceId] = request;
        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        _surfaces.Remove(instanceId);
        return Task.CompletedTask;
    }

    public Task UpdateOrderAsync(Guid instanceId, int order, CancellationToken cancellationToken = default)
    {
        if (_surfaces.TryGetValue(instanceId, out var request))
        {
            _surfaces[instanceId] = request with { Order = order };
        }

        return Task.CompletedTask;
    }

    public Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        CloseAllCalls++;
        _surfaces.Clear();
        return Task.CompletedTask;
    }
}

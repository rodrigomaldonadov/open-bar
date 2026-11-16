using OpenBar.Application.Widgets;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

internal sealed class FakeWidgetWindowService : IWidgetWindowService
{
    private readonly Dictionary<Guid, WidgetWindowRequest> _windows = new();

    public event EventHandler<WidgetWindowCloseRequestedEventArgs>? CloseRequested;

    public IReadOnlyCollection<WidgetWindowRequest> OpenWindows => _windows.Values.ToArray();

    public int CloseAllCalls { get; private set; }

    public bool ThrowOnOpen { get; set; }

    public Task OpenAsync(WidgetWindowRequest request, CancellationToken cancellationToken = default)
    {
        if (ThrowOnOpen)
        {
            throw new InvalidOperationException("Window host failed.");
        }

        _windows[request.InstanceId] = request;
        return Task.CompletedTask;
    }

    public Task CloseAsync(Guid instanceId, CancellationToken cancellationToken = default)
    {
        _windows.Remove(instanceId);
        return Task.CompletedTask;
    }

    public Task CloseAllAsync(CancellationToken cancellationToken = default)
    {
        CloseAllCalls++;
        _windows.Clear();
        return Task.CompletedTask;
    }

    public void RaiseCloseRequested(Guid instanceId)
        => CloseRequested?.Invoke(this, new WidgetWindowCloseRequestedEventArgs(instanceId));
}

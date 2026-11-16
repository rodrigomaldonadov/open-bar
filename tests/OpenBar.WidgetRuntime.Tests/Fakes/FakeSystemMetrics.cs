using OpenBar.SystemBridge;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

internal sealed class FakeSystemMetrics : ISystemMetrics
{
    public SystemMetricsSnapshot Snapshot { get; set; } = SystemMetricsSnapshot.Empty;

    public int RefreshCalls { get; private set; }

    public SystemMetricsSnapshot Current => Snapshot;

    public Task<SystemMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        return Task.FromResult(Snapshot);
    }

    public IDisposable Subscribe(Action<SystemMetricsSnapshot> observer, TimeSpan? interval = null)
        => new EmptyDisposable();

    private sealed class EmptyDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

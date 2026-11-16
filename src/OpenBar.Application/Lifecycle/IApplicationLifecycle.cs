namespace OpenBar.Application.Lifecycle;

/// <summary>
/// Platform-neutral application lifecycle state machine. It does not own the
/// host or any Windows resource; it only coordinates shutdown requests so that
/// multiple callers (tray, future commands, tests) cannot start shutdown twice.
/// </summary>
public interface IApplicationLifecycle : IDisposable
{
    ApplicationLifecycleState State { get; }

    bool IsShuttingDown { get; }

    event EventHandler<ApplicationShutdownRequestedEventArgs>? ShutdownRequested;

    void MarkRunning();

    void RequestShutdown(string reason);

    void MarkStopped();
}

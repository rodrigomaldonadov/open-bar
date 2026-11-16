using Microsoft.Extensions.Logging;

namespace OpenBar.Application.Lifecycle;

/// <summary>
/// Thread-safe, idempotent lifecycle coordinator. <see cref="RequestShutdown"/>
/// may be called from the tray window procedure (UI thread) or any other
/// thread; the first request wins and later requests are ignored.
/// </summary>
public sealed partial class ApplicationLifecycleService : IApplicationLifecycle
{
    private readonly ILogger<ApplicationLifecycleService> _logger;
    private readonly object _gate = new();
    private ApplicationLifecycleState _state = ApplicationLifecycleState.Starting;
    private bool _disposed;

    public ApplicationLifecycleService(ILogger<ApplicationLifecycleService> logger)
    {
        _logger = logger;
    }

    public ApplicationLifecycleState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsShuttingDown => State is ApplicationLifecycleState.ShuttingDown or ApplicationLifecycleState.Stopped;

    public event EventHandler<ApplicationShutdownRequestedEventArgs>? ShutdownRequested;

    public void MarkRunning()
    {
        lock (_gate)
        {
            if (_disposed || _state != ApplicationLifecycleState.Starting)
            {
                return;
            }

            _state = ApplicationLifecycleState.Running;
        }

        LogLifecycleRunning(_logger);
    }

    public void RequestShutdown(string reason)
    {
        EventHandler<ApplicationShutdownRequestedEventArgs>? handler;

        lock (_gate)
        {
            if (_disposed || _state is ApplicationLifecycleState.ShuttingDown or ApplicationLifecycleState.Stopped)
            {
                return;
            }

            _state = ApplicationLifecycleState.ShuttingDown;
            handler = ShutdownRequested;
        }

        LogShutdownRequested(_logger, reason);

        if (handler is null)
        {
            return;
        }

        var args = new ApplicationShutdownRequestedEventArgs(reason);
        foreach (EventHandler<ApplicationShutdownRequestedEventArgs> target in handler.GetInvocationList())
        {
            try
            {
                target(this, args);
            }
            catch (Exception ex)
            {
                // A faulty shutdown handler must not prevent the remaining
                // handlers (or the process) from shutting down cleanly.
                LogShutdownHandlerFailed(_logger, ex);
            }
        }
    }

    public void MarkStopped()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _state = ApplicationLifecycleState.Stopped;
        }

        LogLifecycleStopped(_logger);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ShutdownRequested = null;
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Application lifecycle is running.")]
    private static partial void LogLifecycleRunning(ILogger<ApplicationLifecycleService> logger);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Application shutdown requested. Reason: {Reason}")]
    private static partial void LogShutdownRequested(ILogger<ApplicationLifecycleService> logger, string reason);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Error, Message = "An application shutdown handler failed.")]
    private static partial void LogShutdownHandlerFailed(ILogger<ApplicationLifecycleService> logger, Exception exception);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Information, Message = "Application lifecycle stopped.")]
    private static partial void LogLifecycleStopped(ILogger<ApplicationLifecycleService> logger);
}

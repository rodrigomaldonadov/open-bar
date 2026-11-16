using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Hosted integration. Starts after persistence (registration order) so
/// persisted instances can be recovered, and stops before persistence so
/// widget work and windows are released first.
/// </summary>
public sealed partial class WidgetRuntimeHostedService : IHostedService
{
    private readonly WidgetDiscoveryService _discovery;
    private readonly IWidgetRuntime _runtime;
    private readonly WidgetScheduler _scheduler;
    private readonly ILogger<WidgetRuntimeHostedService> _logger;

    public WidgetRuntimeHostedService(
        WidgetDiscoveryService discovery,
        IWidgetRuntime runtime,
        WidgetScheduler scheduler,
        ILogger<WidgetRuntimeHostedService> logger)
    {
        _discovery = discovery;
        _runtime = runtime;
        _scheduler = scheduler;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.Run(() => StartCoreAsync(cancellationToken), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.Run(() => StopCoreAsync(cancellationToken), cancellationToken);

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var discovery = await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        if (discovery.Failures.Count > 0)
        {
            LogDiscoveryFailures(_logger, discovery.Failures.Count);
        }

        try
        {
            var report = await _runtime.StartAllAsync(cancellationToken).ConfigureAwait(false);

            if (report.Failures.Count > 0)
            {
                LogInstanceFailures(_logger, report.Failures.Count);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The instance store itself is unavailable: this is a global
            // infrastructure error, so startup must fail loudly.
            LogRuntimeStartFailed(_logger, ex);
            throw;
        }
    }

    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _runtime.StopAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogStopTimedOut(_logger);
        }
        catch (Exception ex)
        {
            LogStopFailed(_logger, ex);
        }
        finally
        {
            // Every per-instance scope is already disposed by StopAllAsync; this
            // releases anything a failed stop left behind.
            await _scheduler.DisposeAsync().ConfigureAwait(false);
        }
    }

    [LoggerMessage(EventId = 5401, Level = LogLevel.Warning, Message = "Widget discovery reported {FailureCount} failure(s); available widgets will still start.")]
    private static partial void LogDiscoveryFailures(ILogger<WidgetRuntimeHostedService> logger, int failureCount);

    [LoggerMessage(EventId = 5402, Level = LogLevel.Warning, Message = "{FailureCount} widget instance(s) failed to start; the rest of the runtime is unaffected.")]
    private static partial void LogInstanceFailures(ILogger<WidgetRuntimeHostedService> logger, int failureCount);

    [LoggerMessage(EventId = 5403, Level = LogLevel.Critical, Message = "The widget runtime could not start.")]
    private static partial void LogRuntimeStartFailed(ILogger<WidgetRuntimeHostedService> logger, Exception exception);

    [LoggerMessage(EventId = 5404, Level = LogLevel.Warning, Message = "Widget runtime shutdown was canceled before every instance stopped.")]
    private static partial void LogStopTimedOut(ILogger<WidgetRuntimeHostedService> logger);

    [LoggerMessage(EventId = 5405, Level = LogLevel.Error, Message = "The widget runtime failed while stopping.")]
    private static partial void LogStopFailed(ILogger<WidgetRuntimeHostedService> logger, Exception exception);
}

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Application-wide scheduler. The runtime creates one scope per widget
/// instance; stopping the instance disposes its scope, so no timer survives a
/// stop or a crash in a widget callback.
/// </summary>
public sealed class WidgetScheduler : IAsyncDisposable
{
    private readonly ConcurrentDictionary<WidgetSchedulerScope, byte> _scopes = new();
    private readonly WidgetSchedulerOptions _options;
    private readonly ILogger<WidgetScheduler> _logger;
    private readonly TimeProvider _timeProvider;
    private int _disposed;

    public WidgetScheduler(
        WidgetSchedulerOptions options,
        TimeProvider timeProvider,
        ILogger<WidgetScheduler> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        options.Validate();
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public WidgetSchedulerScope CreateScope(Guid instanceId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var scope = new WidgetSchedulerScope(instanceId, _options, _timeProvider, _logger, RemoveScope);
        _scopes.TryAdd(scope, 0);
        return scope;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var scopes = _scopes.Keys.ToArray();
        foreach (var scope in scopes)
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }

        _scopes.Clear();
    }

    private void RemoveScope(WidgetSchedulerScope scope) => _scopes.TryRemove(scope, out _);
}

using Microsoft.Extensions.Logging;

namespace OpenBar.SystemBridge;

/// <summary>Shared battery sampling with a short cache and single-flight gate.</summary>
public sealed partial class BatteryMetricsService : IBatteryMetrics, IAsyncDisposable
{
    private readonly IBatteryMetricsSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatteryMetricsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private BatteryMetricsSnapshot _current = BatteryMetricsSnapshot.Empty;
    private DateTimeOffset _capturedAt = DateTimeOffset.MinValue;
    private int _disposed;

    public BatteryMetricsService(
        IBatteryMetricsSource source,
        TimeProvider timeProvider,
        ILogger<BatteryMetricsService> logger)
    {
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public BatteryMetricsSnapshot Current => Volatile.Read(ref _current);

    public async Task<BatteryMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var now = _timeProvider.GetUtcNow();
        if (now - _capturedAt < TimeSpan.FromSeconds(1))
        {
            return Current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (now - _capturedAt < TimeSpan.FromSeconds(1))
            {
                return Current;
            }

            var snapshot = Capture(now);
            _capturedAt = now;
            Volatile.Write(ref _current, snapshot);
            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _gate.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private BatteryMetricsSnapshot Capture(DateTimeOffset now)
    {
        try
        {
            if (!_source.TryRead(out var sample) || !sample.IsAvailable)
            {
                return new BatteryMetricsSnapshot(false, null, false, sample.IsOnAcPower, now);
            }

            var percent = Math.Clamp(sample.Percent, 0, 100);
            return new BatteryMetricsSnapshot(true, percent, sample.IsCharging, sample.IsOnAcPower, now);
        }
        catch (Exception ex)
        {
            LogReadFailed(_logger, ex);
            return new BatteryMetricsSnapshot(false, null, false, false, now);
        }
    }

    [LoggerMessage(EventId = 9201, Level = LogLevel.Warning, Message = "Battery metrics could not be read.")]
    private static partial void LogReadFailed(ILogger<BatteryMetricsService> logger, Exception exception);
}

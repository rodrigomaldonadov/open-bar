using Microsoft.Extensions.Logging;

namespace OpenBar.SystemBridge;

/// <summary>
/// Shared network counters. The first sample for an adapter establishes the
/// baseline and reports no speed; later samples use byte/time deltas.
/// </summary>
public sealed partial class NetworkMetricsService : INetworkMetrics, IAsyncDisposable
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMilliseconds(500);

    private readonly INetworkMetricsSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NetworkMetricsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, Baseline> _baselines = new(StringComparer.OrdinalIgnoreCase);

    private NetworkMetricsSnapshot _current = NetworkMetricsSnapshot.Empty;
    private string? _currentAdapterId;
    private DateTimeOffset _capturedAt = DateTimeOffset.MinValue;
    private int _disposed;

    public NetworkMetricsService(
        INetworkMetricsSource source,
        TimeProvider timeProvider,
        ILogger<NetworkMetricsService> logger)
    {
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public NetworkMetricsSnapshot Current => Volatile.Read(ref _current);

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters()
    {
        try
        {
            return _source.GetAdapters();
        }
        catch (Exception ex)
        {
            LogReadFailed(_logger, ex);
            return [];
        }
    }

    public async Task<NetworkMetricsSnapshot> RefreshAsync(
        string? adapterId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var now = _timeProvider.GetUtcNow();
        if (string.Equals(adapterId, _currentAdapterId, StringComparison.OrdinalIgnoreCase)
            && now - _capturedAt < CacheDuration)
        {
            return Current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (string.Equals(adapterId, _currentAdapterId, StringComparison.OrdinalIgnoreCase)
                && now - _capturedAt < CacheDuration)
            {
                return Current;
            }

            var snapshot = Capture(adapterId, now);
            _currentAdapterId = adapterId;
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

    private NetworkMetricsSnapshot Capture(string? adapterId, DateTimeOffset now)
    {
        try
        {
            if (!_source.TryRead(adapterId, out var sample))
            {
                return new NetworkMetricsSnapshot(false, null, null, null, now);
            }

            double? download = null;
            double? upload = null;

            lock (_baselines)
            {
                if (_baselines.TryGetValue(sample.Adapter.Id, out var previous))
                {
                    var elapsed = (now - previous.CapturedAt).TotalSeconds;
                    if (elapsed > 0.01)
                    {
                        var received = Math.Max(0, sample.ReceivedBytes - previous.ReceivedBytes);
                        var sent = Math.Max(0, sample.SentBytes - previous.SentBytes);
                        download = received / elapsed;
                        upload = sent / elapsed;
                    }
                }

                _baselines[sample.Adapter.Id] = new Baseline(
                    sample.ReceivedBytes,
                    sample.SentBytes,
                    now);
            }

            return new NetworkMetricsSnapshot(
                true,
                sample.Adapter,
                download,
                upload,
                now);
        }
        catch (Exception ex)
        {
            LogReadFailed(_logger, ex);
            return new NetworkMetricsSnapshot(false, null, null, null, now);
        }
    }

    private readonly record struct Baseline(long ReceivedBytes, long SentBytes, DateTimeOffset CapturedAt);

    [LoggerMessage(EventId = 9401, Level = LogLevel.Warning, Message = "Network metrics could not be read.")]
    private static partial void LogReadFailed(ILogger<NetworkMetricsService> logger, Exception exception);
}

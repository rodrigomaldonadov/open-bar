using Microsoft.Extensions.Logging;

namespace OpenBar.SystemBridge;

/// <summary>Shared storage sampling. Volumes come from DriveInfo, never a filesystem walk.</summary>
public sealed partial class StorageMetricsService : IStorageMetrics, IAsyncDisposable
{
    private readonly IStorageMetricsSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StorageMetricsService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private StorageMetricsSnapshot _current = StorageMetricsSnapshot.Empty;
    private string? _currentVolumeId;
    private DateTimeOffset _capturedAt = DateTimeOffset.MinValue;
    private int _disposed;

    public StorageMetricsService(
        IStorageMetricsSource source,
        TimeProvider timeProvider,
        ILogger<StorageMetricsService> logger)
    {
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public StorageMetricsSnapshot Current => Volatile.Read(ref _current);

    public IReadOnlyList<StorageVolumeInfo> GetVolumes()
    {
        try
        {
            return _source.GetVolumes();
        }
        catch (Exception ex)
        {
            LogReadFailed(_logger, ex);
            return [];
        }
    }

    public async Task<StorageMetricsSnapshot> RefreshAsync(
        string? volumeId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var now = _timeProvider.GetUtcNow();
        if (string.Equals(volumeId, _currentVolumeId, StringComparison.OrdinalIgnoreCase)
            && now - _capturedAt < TimeSpan.FromSeconds(2))
        {
            return Current;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (string.Equals(volumeId, _currentVolumeId, StringComparison.OrdinalIgnoreCase)
                && now - _capturedAt < TimeSpan.FromSeconds(2))
            {
                return Current;
            }

            var snapshot = Capture(volumeId, now);
            _currentVolumeId = volumeId;
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

    private StorageMetricsSnapshot Capture(string? volumeId, DateTimeOffset now)
    {
        try
        {
            var volumes = _source.GetVolumes();
            var selected = string.IsNullOrWhiteSpace(volumeId)
                ? volumes.FirstOrDefault(volume => volume.TotalBytes > 0)
                : volumes.FirstOrDefault(volume => string.Equals(volume.Id, volumeId, StringComparison.OrdinalIgnoreCase));

            if (selected is null || selected.TotalBytes == 0)
            {
                return new StorageMetricsSnapshot(false, null, null, null, now);
            }

            var free = Math.Min(selected.FreeBytes, selected.TotalBytes);
            var used = selected.TotalBytes - free;
            var percent = Math.Clamp((double)used / selected.TotalBytes * 100.0, 0, 100);

            return new StorageMetricsSnapshot(true, selected, used, percent, now);
        }
        catch (Exception ex)
        {
            LogReadFailed(_logger, ex);
            return new StorageMetricsSnapshot(false, null, null, null, now);
        }
    }

    [LoggerMessage(EventId = 9301, Level = LogLevel.Warning, Message = "Storage metrics could not be read.")]
    private static partial void LogReadFailed(ILogger<StorageMetricsService> logger, Exception exception);
}

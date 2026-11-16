namespace OpenBar.SystemBridge;

public sealed record StorageVolumeInfo(
    string Id,
    string Name,
    string RootPath,
    ulong TotalBytes,
    ulong FreeBytes);

public sealed record StorageMetricsSnapshot(
    bool IsAvailable,
    StorageVolumeInfo? Volume,
    ulong? UsedBytes,
    double? UsagePercent,
    DateTimeOffset CapturedAtUtc)
{
    public static StorageMetricsSnapshot Empty { get; } = new(
        false,
        null,
        null,
        null,
        DateTimeOffset.MinValue);
}

/// <summary>Efficient volume enumeration (DriveInfo, no recursive scan).</summary>
public interface IStorageMetricsSource
{
    IReadOnlyList<StorageVolumeInfo> GetVolumes();
}

/// <summary>Shared, cached storage measurements.</summary>
public interface IStorageMetrics
{
    StorageMetricsSnapshot Current { get; }

    IReadOnlyList<StorageVolumeInfo> GetVolumes();

    Task<StorageMetricsSnapshot> RefreshAsync(
        string? volumeId = null,
        CancellationToken cancellationToken = default);
}

public sealed class NullStorageMetrics : IStorageMetrics
{
    public static NullStorageMetrics Instance { get; } = new();

    public StorageMetricsSnapshot Current => StorageMetricsSnapshot.Empty;

    public IReadOnlyList<StorageVolumeInfo> GetVolumes() => [];

    public Task<StorageMetricsSnapshot> RefreshAsync(
        string? volumeId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Current);
}

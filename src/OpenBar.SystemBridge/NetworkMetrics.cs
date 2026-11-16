namespace OpenBar.SystemBridge;

public sealed record NetworkAdapterInfo(string Id, string Name, string Description);

/// <summary>Cumulative counters for one adapter.</summary>
public readonly record struct NetworkSample(
    NetworkAdapterInfo Adapter,
    long ReceivedBytes,
    long SentBytes);

public sealed record NetworkMetricsSnapshot(
    bool IsAvailable,
    NetworkAdapterInfo? Adapter,
    double? DownloadBytesPerSecond,
    double? UploadBytesPerSecond,
    DateTimeOffset CapturedAtUtc)
{
    public static NetworkMetricsSnapshot Empty { get; } = new(
        false,
        null,
        null,
        null,
        DateTimeOffset.MinValue);
}

/// <summary>Native/OS network source, replaceable in tests.</summary>
public interface INetworkMetricsSource
{
    IReadOnlyList<NetworkAdapterInfo> GetAdapters();

    bool TryRead(string? adapterId, out NetworkSample sample);
}

/// <summary>
/// Shared network measurements. Speeds are computed from cumulative counters;
/// the first sample for an adapter reports unavailable speeds.
/// </summary>
public interface INetworkMetrics
{
    NetworkMetricsSnapshot Current { get; }

    IReadOnlyList<NetworkAdapterInfo> GetAdapters();

    Task<NetworkMetricsSnapshot> RefreshAsync(
        string? adapterId = null,
        CancellationToken cancellationToken = default);
}

public sealed class NullNetworkMetrics : INetworkMetrics
{
    public static NullNetworkMetrics Instance { get; } = new();

    public NetworkMetricsSnapshot Current => NetworkMetricsSnapshot.Empty;

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => [];

    public Task<NetworkMetricsSnapshot> RefreshAsync(
        string? adapterId = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Current);
}

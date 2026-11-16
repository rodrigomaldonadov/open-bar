using OpenBar.SystemBridge;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

internal sealed class FakeBatteryMetrics : IBatteryMetrics
{
    public BatteryMetricsSnapshot Snapshot { get; set; } = BatteryMetricsSnapshot.Empty;

    public int RefreshCalls { get; private set; }

    public BatteryMetricsSnapshot Current => Snapshot;

    public Task<BatteryMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        return Task.FromResult(Snapshot);
    }
}

internal sealed class FakeNetworkMetrics : INetworkMetrics
{
    public NetworkMetricsSnapshot Snapshot { get; set; } = NetworkMetricsSnapshot.Empty;

    public int RefreshCalls { get; private set; }

    public string? LastAdapterId { get; private set; }

    public NetworkMetricsSnapshot Current => Snapshot;

    public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => [];

    public Task<NetworkMetricsSnapshot> RefreshAsync(
        string? adapterId = null,
        CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        LastAdapterId = adapterId;
        return Task.FromResult(Snapshot);
    }
}

internal sealed class FakeStorageMetrics : IStorageMetrics
{
    public StorageMetricsSnapshot Snapshot { get; set; } = StorageMetricsSnapshot.Empty;

    public int RefreshCalls { get; private set; }

    public string? LastVolumeId { get; private set; }

    public StorageMetricsSnapshot Current => Snapshot;

    public IReadOnlyList<StorageVolumeInfo> GetVolumes() => [];

    public Task<StorageMetricsSnapshot> RefreshAsync(
        string? volumeId = null,
        CancellationToken cancellationToken = default)
    {
        RefreshCalls++;
        LastVolumeId = volumeId;
        return Task.FromResult(Snapshot);
    }
}

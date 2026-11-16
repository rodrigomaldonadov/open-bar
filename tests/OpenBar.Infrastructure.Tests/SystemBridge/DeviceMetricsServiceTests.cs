using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.SystemBridge;
using Xunit;

namespace OpenBar.Infrastructure.Tests.SystemBridge;

public sealed class DeviceMetricsServiceTests
{
    [Fact]
    public async Task BatteryClampsThePercentageAndReportsCharging()
    {
        var source = new FakeBatterySource
        {
            Sample = new BatterySample(true, 150, true, true)
        };
        await using var service = CreateBattery(source);

        var snapshot = await service.RefreshAsync();

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(100, snapshot.Percent);
        Assert.True(snapshot.IsCharging);
        Assert.True(snapshot.IsOnAcPower);
    }

    [Fact]
    public async Task BatteryWithoutHardwareIsUnavailable()
    {
        var source = new FakeBatterySource
        {
            Sample = new BatterySample(false, 0, false, true)
        };
        await using var service = CreateBattery(source);

        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.IsAvailable);
        Assert.Null(snapshot.Percent);
    }

    [Fact]
    public async Task BatterySourceFailuresAreIsolated()
    {
        var source = new FakeBatterySource { Throw = true };
        await using var service = CreateBattery(source);

        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.IsAvailable);
    }

    [Fact]
    public async Task NetworkFirstSampleHasNoSpeedAndLaterSamplesUseDeltas()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var source = new FakeNetworkSource();
        source.Samples.Enqueue(new NetworkSample(source.Adapter, 1_000, 500));
        source.Samples.Enqueue(new NetworkSample(source.Adapter, 3_000, 1_500));
        await using var service = CreateNetwork(source, time);

        var first = await service.RefreshAsync();
        Assert.True(first.IsAvailable);
        Assert.Null(first.DownloadBytesPerSecond);
        Assert.Null(first.UploadBytesPerSecond);

        time.Advance(TimeSpan.FromSeconds(1));
        var second = await service.RefreshAsync();

        Assert.Equal(2_000, second.DownloadBytesPerSecond!.Value, 3);
        Assert.Equal(1_000, second.UploadBytesPerSecond!.Value, 3);
    }

    [Fact]
    public async Task NetworkAdapterChangeResetsTheBaseline()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
        var source = new FakeNetworkSource();
        var secondAdapter = new NetworkAdapterInfo("second", "Wi-Fi", "Wireless");
        source.Samples.Enqueue(new NetworkSample(source.Adapter, 1_000, 500));
        source.Samples.Enqueue(new NetworkSample(secondAdapter, 5_000, 2_000));
        await using var service = CreateNetwork(source, time);

        _ = await service.RefreshAsync(source.Adapter.Id);
        time.Advance(TimeSpan.FromSeconds(1));
        var switched = await service.RefreshAsync(secondAdapter.Id);

        Assert.True(switched.IsAvailable);
        Assert.Null(switched.DownloadBytesPerSecond);
        Assert.Equal(secondAdapter, switched.Adapter);
    }

    [Fact]
    public async Task MissingNetworkAdapterIsUnavailable()
    {
        var source = new FakeNetworkSource { HasData = false };
        await using var service = CreateNetwork(source, new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.IsAvailable);
    }

    [Fact]
    public async Task StorageSelectsAVolumeAndComputesUsage()
    {
        var volume = new StorageVolumeInfo(@"C:\", "System", @"C:\", 1_000, 250);
        var source = new FakeStorageSource(volume);
        await using var service = CreateStorage(source);

        var snapshot = await service.RefreshAsync(@"C:\");

        Assert.True(snapshot.IsAvailable);
        Assert.Equal(750ul, snapshot.UsedBytes);
        Assert.Equal(75, snapshot.UsagePercent);
    }

    [Fact]
    public async Task StorageUnknownVolumeIsUnavailable()
    {
        var source = new FakeStorageSource(
            new StorageVolumeInfo(@"C:\", "System", @"C:\", 1_000, 250));
        await using var service = CreateStorage(source);

        var snapshot = await service.RefreshAsync(@"Z:\");

        Assert.False(snapshot.IsAvailable);
    }

    private static BatteryMetricsService CreateBattery(IBatteryMetricsSource source)
        => new(source, TimeProvider.System, NullLogger<BatteryMetricsService>.Instance);

    private static NetworkMetricsService CreateNetwork(INetworkMetricsSource source, TimeProvider time)
        => new(source, time, NullLogger<NetworkMetricsService>.Instance);

    private static StorageMetricsService CreateStorage(IStorageMetricsSource source)
        => new(source, TimeProvider.System, NullLogger<StorageMetricsService>.Instance);

    private sealed class FakeBatterySource : IBatteryMetricsSource
    {
        public BatterySample Sample { get; set; }

        public bool Throw { get; set; }

        public bool TryRead(out BatterySample sample)
        {
            if (Throw)
            {
                throw new InvalidOperationException("battery failure");
            }

            sample = Sample;
            return true;
        }
    }

    private sealed class FakeNetworkSource : INetworkMetricsSource
    {
        public NetworkAdapterInfo Adapter { get; } = new("primary", "Ethernet", "Ethernet adapter");

        public Queue<NetworkSample> Samples { get; } = new();

        public bool HasData { get; set; } = true;

        public IReadOnlyList<NetworkAdapterInfo> GetAdapters() => [Adapter];

        public bool TryRead(string? adapterId, out NetworkSample sample)
        {
            if (!HasData || Samples.Count == 0)
            {
                sample = default;
                return false;
            }

            sample = Samples.Dequeue();
            return true;
        }
    }

    private sealed class FakeStorageSource : IStorageMetricsSource
    {
        private readonly StorageVolumeInfo[] _volumes;

        public FakeStorageSource(params StorageVolumeInfo[] volumes)
            => _volumes = volumes;

        public IReadOnlyList<StorageVolumeInfo> GetVolumes() => _volumes;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan delta) => _now += delta;
    }
}

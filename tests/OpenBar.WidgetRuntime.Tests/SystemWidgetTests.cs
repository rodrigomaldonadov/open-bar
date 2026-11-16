using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.SystemBridge;
using OpenBar.WidgetRuntime.Tests.Fakes;
using OpenBar.Widgets.Abstractions;
using OpenBar.Widgets.System;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class SystemWidgetTests
{
    [Fact]
    public void DefinitionsExposeTheExpectedIdsAndMetadata()
    {
        var cpu = CpuWidgetDefinition.Create();
        var memory = MemoryWidgetDefinition.Create();

        Assert.Equal("openbar.cpu", cpu.WidgetId);
        Assert.Equal("openbar.memory", memory.WidgetId);
        Assert.True(cpu.Metadata.Capabilities.HasFlag(WidgetCapabilities.PeriodicUpdates));
        Assert.True(memory.Metadata.Capabilities.HasFlag(WidgetCapabilities.Settings));
        Assert.True(cpu.Metadata.AllowsMultipleInstances);
        Assert.True(memory.Metadata.AllowsMultipleInstances);
    }

    [Fact]
    public async Task CpuWidgetUsesTheSharedMetricsServiceAndRuntimeScheduler()
    {
        var scheduler = new RecordingScheduler();
        var metrics = new FakeSystemMetrics
        {
            Snapshot = Snapshot(cpu: 42, memory: 60)
        };
        var widget = new CpuWidget();

        await widget.InitializeAsync(CreateContext(scheduler, metrics, """{"intervalSeconds":3}"""));
        await widget.StartAsync();

        Assert.Equal(1, metrics.RefreshCalls);
        var first = Assert.Single(scheduler.Schedules);
        Assert.Equal("cpu.sample", first.Name);
        Assert.Equal(TimeSpan.FromSeconds(3), first.Interval);

        await widget.OnSettingsChangedAsync(WidgetSettings.Parse("""{"intervalSeconds":5}""", 1));

        Assert.Equal(2, scheduler.Schedules.Count);
        Assert.True(scheduler.Schedules[0].IsDisposed);
        Assert.Equal(TimeSpan.FromSeconds(5), scheduler.Schedules[1].Interval);

        await widget.StopAsync();

        Assert.True(scheduler.Schedules[1].IsDisposed);
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task MemoryWidgetUsesTheSharedMetricsServiceAndRuntimeScheduler()
    {
        var scheduler = new RecordingScheduler();
        var metrics = new FakeSystemMetrics
        {
            Snapshot = Snapshot(cpu: 10, memory: 55)
        };
        var widget = new MemoryWidget();

        await widget.InitializeAsync(CreateContext(scheduler, metrics, """{"intervalSeconds":4,"unit":"mb"}"""));
        await widget.StartAsync();

        Assert.Equal(1, metrics.RefreshCalls);
        var schedule = Assert.Single(scheduler.Schedules);
        Assert.Equal("memory.sample", schedule.Name);
        Assert.Equal(TimeSpan.FromSeconds(4), schedule.Interval);

        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task InvalidWidgetSettingsFallBackToDefaults()
    {
        var scheduler = new RecordingScheduler();
        var metrics = new FakeSystemMetrics();
        var widget = new CpuWidget();

        await widget.InitializeAsync(CreateContext(scheduler, metrics, """{"intervalSeconds":"bad"}"""));
        await widget.StartAsync();

        var schedule = Assert.Single(scheduler.Schedules);
        Assert.Equal(TimeSpan.FromSeconds(1), schedule.Interval);

        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task BatteryWidgetUsesTheSharedBatteryServiceAndScheduler()
    {
        var scheduler = new RecordingScheduler();
        var battery = new FakeBatteryMetrics
        {
            Snapshot = new BatteryMetricsSnapshot(true, 87, false, false, DateTimeOffset.UtcNow)
        };
        var widget = new BatteryWidget();

        await widget.InitializeAsync(CreateContext(
            scheduler,
            new FakeSystemMetrics(),
            """{"intervalSeconds":4}""",
            battery: battery));
        await widget.StartAsync();

        Assert.Equal(1, battery.RefreshCalls);
        Assert.Equal("battery.sample", Assert.Single(scheduler.Schedules).Name);
        Assert.Equal(TimeSpan.FromSeconds(4), scheduler.Schedules[0].Interval);

        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task NetworkWidgetPassesTheSelectedAdapter()
    {
        var scheduler = new RecordingScheduler();
        var network = new FakeNetworkMetrics
        {
            Snapshot = new NetworkMetricsSnapshot(
                true,
                new NetworkAdapterInfo("eth0", "Ethernet", "Ethernet"),
                1024,
                512,
                DateTimeOffset.UtcNow)
        };
        var widget = new NetworkWidget();

        await widget.InitializeAsync(CreateContext(
            scheduler,
            new FakeSystemMetrics(),
            """{"intervalSeconds":3,"adapterId":"eth0"}""",
            network: network));
        await widget.StartAsync();

        Assert.Equal("eth0", network.LastAdapterId);
        Assert.Equal(TimeSpan.FromSeconds(3), Assert.Single(scheduler.Schedules).Interval);

        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task StorageWidgetPassesTheSelectedVolume()
    {
        var scheduler = new RecordingScheduler();
        var volume = new StorageVolumeInfo(@"C:\", "System", @"C:\", 1_000, 250);
        var storage = new FakeStorageMetrics
        {
            Snapshot = new StorageMetricsSnapshot(true, volume, 750, 75, DateTimeOffset.UtcNow)
        };
        var widget = new StorageWidget();

        await widget.InitializeAsync(CreateContext(
            scheduler,
            new FakeSystemMetrics(),
            """{"intervalSeconds":5,"volumeId":"C:\\"}""",
            storage: storage));
        await widget.StartAsync();

        Assert.Equal(@"C:\", storage.LastVolumeId);
        Assert.Equal(TimeSpan.FromSeconds(5), Assert.Single(scheduler.Schedules).Interval);

        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    [Fact]
    public void NewSystemDefinitionsExposeCompactDockMetadata()
    {
        var battery = BatteryWidgetDefinition.Create();
        var network = NetworkWidgetDefinition.Create();
        var storage = StorageWidgetDefinition.Create();

        Assert.Equal("openbar.battery", battery.WidgetId);
        Assert.Equal("openbar.network", network.WidgetId);
        Assert.Equal("openbar.storage", storage.WidgetId);
        Assert.True(battery.Metadata.CompactWidth > 0);
        Assert.True(network.Metadata.CompactHeight > 0);
        Assert.True(storage.Metadata.Capabilities.HasFlag(WidgetCapabilities.Settings));
    }

    private static WidgetContext CreateContext(
        IWidgetScheduler scheduler,
        ISystemMetrics metrics,
        string json,
        IBatteryMetrics? battery = null,
        INetworkMetrics? network = null,
        IStorageMetrics? storage = null)
        => new()
        {
            InstanceId = Guid.NewGuid(),
            WidgetId = "test",
            InstanceName = "System widget",
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
            Metrics = metrics,
            Battery = battery ?? NullBatteryMetrics.Instance,
            Network = network ?? NullNetworkMetrics.Instance,
            Storage = storage ?? NullStorageMetrics.Instance,
            Settings = WidgetSettings.Parse(json, 1)
        };

    private static SystemMetricsSnapshot Snapshot(double cpu, double memory)
        => new(
            cpu,
            16UL * 1024 * 1024 * 1024,
            10UL * 1024 * 1024 * 1024,
            6UL * 1024 * 1024 * 1024,
            memory,
            CpuAvailable: true,
            MemoryAvailable: true,
            DateTimeOffset.UtcNow);
}

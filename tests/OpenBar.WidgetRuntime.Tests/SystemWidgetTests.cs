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

    private static WidgetContext CreateContext(
        IWidgetScheduler scheduler,
        ISystemMetrics metrics,
        string json)
        => new()
        {
            InstanceId = Guid.NewGuid(),
            WidgetId = "test",
            InstanceName = "System widget",
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
            Metrics = metrics,
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

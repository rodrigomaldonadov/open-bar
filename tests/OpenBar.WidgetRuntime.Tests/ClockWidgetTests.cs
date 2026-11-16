using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.WidgetRuntime.Tests.Fakes;
using OpenBar.Widgets.Abstractions;
using OpenBar.Widgets.Clock;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class ClockWidgetTests
{
    [Fact]
    public void DefinitionDeclaresTheExpectedMetadata()
    {
        var definition = ClockWidgetDefinition.Create();

        Assert.Equal("openbar.clock", definition.WidgetId);
        Assert.Equal("Reloj", definition.Metadata.DisplayName);
        Assert.True(definition.Metadata.AllowsMultipleInstances);
        Assert.True(definition.Metadata.Capabilities.HasFlag(WidgetCapabilities.Settings));
        Assert.True(definition.Metadata.Capabilities.HasFlag(WidgetCapabilities.PeriodicUpdates));
        Assert.IsAssignableFrom<IWidget>(definition.CreateWidget());
    }

    [Fact]
    public async Task StartUsesTheSharedSchedulerAndStopDisposesIt()
    {
        var scheduler = new RecordingScheduler();
        var widget = new ClockWidget();
        var context = CreateContext(scheduler);

        await widget.InitializeAsync(context);
        await widget.StartAsync();

        var schedule = Assert.Single(scheduler.Schedules);
        Assert.Equal("clock.tick", schedule.Name);
        Assert.Equal(TimeSpan.FromSeconds(1), schedule.Interval);
        Assert.False(schedule.IsDisposed);

        await widget.StopAsync();

        Assert.True(schedule.IsDisposed);
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task SettingsUpdatesAreAcceptedWithoutCreatingAView()
    {
        var scheduler = new RecordingScheduler();
        var widget = new ClockWidget();

        await widget.InitializeAsync(CreateContext(scheduler, """{"use24Hour":false,"showSeconds":false}"""));
        await widget.StartAsync();
        await widget.OnSettingsChangedAsync(WidgetSettings.Parse("""{"use24Hour":true}""", 1));

        Assert.Single(scheduler.Schedules);
        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task InvalidSettingsFallBackToDefaults()
    {
        var scheduler = new RecordingScheduler();
        var widget = new ClockWidget();

        await widget.InitializeAsync(CreateContext(scheduler, """{"use24Hour":"not-a-boolean"}"""));
        await widget.StartAsync();

        Assert.Single(scheduler.Schedules);

        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    [Fact]
    public async Task UnknownJsonPropertiesAreIgnored()
    {
        var scheduler = new RecordingScheduler();
        var widget = new ClockWidget();

        await widget.InitializeAsync(CreateContext(scheduler, """{"unknown":123}"""));
        await widget.StartAsync();

        Assert.Single(scheduler.Schedules);

        await widget.StopAsync();
        await widget.DisposeAsync();
    }

    private static WidgetContext CreateContext(IWidgetScheduler scheduler, string json = ClockWidgetDefinition.DefaultSettingsJson)
        => new()
        {
            InstanceId = Guid.NewGuid(),
            WidgetId = ClockWidgetDefinition.WidgetId,
            InstanceName = "Clock",
            Logger = NullLogger.Instance,
            Scheduler = scheduler,
            Settings = WidgetSettings.Parse(json, 1)
        };
}

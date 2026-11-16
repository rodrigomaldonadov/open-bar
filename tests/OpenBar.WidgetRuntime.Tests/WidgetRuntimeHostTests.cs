using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Widgets;
using OpenBar.Domain.Widgets;
using OpenBar.Infrastructure.Persistence;
using OpenBar.WidgetRuntime.Tests.Fakes;
using OpenBar.Widgets.Abstractions;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class WidgetRuntimeHostTests
{
    [Fact]
    public async Task HostStartCreatesTheDatabaseAndStartsWithNoInstances()
    {
        await using var database = new RuntimeDatabase();
        var factory = new RecordingWidgetFactory();
        var definition = TestDefinitions.Create(factory: factory.Create);

        using var host = BuildHost(database.Options, new FakeWidgetWindowService(), definition);
        await host.StartAsync();

        Assert.True(File.Exists(database.Options.DatabasePath));
        Assert.Empty(host.Services.GetRequiredService<IWidgetRuntime>().GetInstances());
        Assert.Empty(factory.Created);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostRecoversPersistedInstancesAndStopsThemOnShutdown()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var store = database.CreateInstanceService();
        var seeded = await store.CreateAsync(new WidgetInstance
        {
            WidgetId = TestDefinitions.WidgetId,
            InstanceName = "Recovered",
            IsEnabled = true,
            Width = 260,
            Height = 150,
            SettingsJson = """{"value":5}""",
            SettingsSchemaVersion = 1
        });

        var factory = new RecordingWidgetFactory();
        var windows = new FakeWidgetWindowService();

        using (var host = BuildHost(database.Options, windows, TestDefinitions.Create(factory: factory.Create)))
        {
            await host.StartAsync();

            var runtime = host.Services.GetRequiredService<IWidgetRuntime>();
            var info = Assert.Single(runtime.GetInstances());

            Assert.Equal(seeded.Id, info.Id);
            Assert.Equal(WidgetInstanceState.Running, info.State);
            Assert.Equal(1, factory.Last.StartCalls);
            Assert.Single(windows.OpenWindows);

            await host.StopAsync();
        }

        Assert.Equal(1, factory.Last.StopCalls);
        Assert.Equal(1, factory.Last.DisposeCalls);
        Assert.Empty(windows.OpenWindows);
        Assert.NotNull(await store.GetAsync(seeded.Id));
    }

    [Fact]
    public async Task HostRestartRecoversTheSamePersistedInstance()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var store = database.CreateInstanceService();
        var seeded = await store.CreateAsync(new WidgetInstance
        {
            WidgetId = TestDefinitions.WidgetId,
            InstanceName = "Persistent",
            IsEnabled = true,
            Width = 260,
            Height = 150,
            SettingsJson = "{}",
            SettingsSchemaVersion = 1
        });

        var firstFactory = new RecordingWidgetFactory();
        using (var host = BuildHost(database.Options, new FakeWidgetWindowService(), TestDefinitions.Create(factory: firstFactory.Create)))
        {
            await host.StartAsync();
            await host.StopAsync();
        }

        var secondFactory = new RecordingWidgetFactory();
        using (var host = BuildHost(database.Options, new FakeWidgetWindowService(), TestDefinitions.Create(factory: secondFactory.Create)))
        {
            await host.StartAsync();

            var runtime = host.Services.GetRequiredService<IWidgetRuntime>();
            Assert.Equal(WidgetInstanceState.Running, runtime.GetInstance(seeded.Id)!.State);
            Assert.Equal(1, secondFactory.Last.StartCalls);

            await host.StopAsync();
        }

        Assert.Equal(1, firstFactory.Last.StartCalls);
        Assert.Equal(1, secondFactory.Last.StartCalls);
        Assert.Single(await store.GetAllAsync());
    }

    [Fact]
    public async Task HostDoesNotStartDisabledInstances()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var store = database.CreateInstanceService();
        _ = await store.CreateAsync(new WidgetInstance
        {
            WidgetId = TestDefinitions.WidgetId,
            InstanceName = "Disabled",
            IsEnabled = false,
            Width = 260,
            Height = 150,
            SettingsJson = "{}",
            SettingsSchemaVersion = 1
        });

        var factory = new RecordingWidgetFactory();
        using var host = BuildHost(
            database.Options,
            new FakeWidgetWindowService(),
            TestDefinitions.Create(factory: factory.Create));

        await host.StartAsync();

        var info = Assert.Single(host.Services.GetRequiredService<IWidgetRuntime>().GetInstances());
        Assert.Equal(WidgetInstanceState.Created, info.State);
        Assert.Empty(factory.Created);

        await host.StopAsync();
    }

    private static IHost BuildHost(
        OpenBarDatabaseOptions options,
        FakeWidgetWindowService windows,
        WidgetDefinition definition)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenBarPersistence(options);
        builder.Services.AddSingleton<IWidgetWindowService>(windows);
        builder.Services.AddSingleton(definition);
        builder.Services.AddOpenBarWidgetRuntime();
        return builder.Build();
    }
}

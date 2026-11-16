using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Domain.Widgets;
using OpenBar.Infrastructure.Persistence;
using OpenBar.WidgetRuntime.Tests.Fakes;
using OpenBar.Widgets.Abstractions;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class WidgetRuntimePersistenceTests
{
    [Fact]
    public async Task CreatedInstancesArePersistedAndRecoveredAfterARestart()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var factory = new RecordingWidgetFactory();
        var definition = TestDefinitions.Create(factory: factory.Create);
        Guid instanceId;

        await using (var scheduler = CreateScheduler())
        await using (var runtime = CreateRuntime(database, definition, scheduler, out _))
        {
            var info = await runtime.CreateAsync(TestDefinitions.WidgetId, settingsJson: """{"value":7}""");
            instanceId = info.Id;
        }

        var store = database.CreateInstanceService();
        var persisted = await store.GetAsync(instanceId);
        Assert.NotNull(persisted);
        Assert.Equal("""{"value":7}""", persisted.SettingsJson);

        var recoveryFactory = new RecordingWidgetFactory();
        var recoveryDefinition = TestDefinitions.Create(factory: recoveryFactory.Create);
        var windows = new FakeWidgetWindowService();

        await using var recoveryScheduler = CreateScheduler();
        await using var recoveryRuntime = CreateRuntime(
            database,
            recoveryDefinition,
            recoveryScheduler,
            out _,
            windows);

        var report = await recoveryRuntime.StartAllAsync();

        Assert.Equal(1, report.Started);
        Assert.Empty(report.Failures);
        Assert.Equal(WidgetInstanceState.Running, recoveryRuntime.GetInstance(instanceId)!.State);
        Assert.Single(windows.OpenWindows);
        Assert.Equal(1, recoveryFactory.Last.StartCalls);
    }

    [Fact]
    public async Task UpdatingSettingsAndDisablingPersistToSqlite()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var definition = TestDefinitions.Create();
        await using var scheduler = CreateScheduler();
        await using var runtime = CreateRuntime(database, definition, scheduler, out _);
        var info = await runtime.CreateAsync(TestDefinitions.WidgetId);

        await runtime.UpdateSettingsAsync(info.Id, """{"value":11}""", 3);
        await runtime.SetEnabledAsync(info.Id, false);

        var store = database.CreateInstanceService();
        var persisted = await store.GetAsync(info.Id);

        Assert.NotNull(persisted);
        Assert.Equal("""{"value":11}""", persisted.SettingsJson);
        Assert.Equal(3, persisted.SettingsSchemaVersion);
        Assert.False(persisted.IsEnabled);
    }

    [Fact]
    public async Task DeletingRemovesTheSqliteRow()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var definition = TestDefinitions.Create();
        await using var scheduler = CreateScheduler();
        await using var runtime = CreateRuntime(database, definition, scheduler, out _);
        var info = await runtime.CreateAsync(TestDefinitions.WidgetId);

        Assert.True(await runtime.DeleteAsync(info.Id));

        var store = database.CreateInstanceService();
        Assert.Null(await store.GetAsync(info.Id));
        Assert.Empty(await store.GetAllAsync());
    }

    [Fact]
    public async Task MultipleInstancesOfTheSameWidgetPersistIndependentJson()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var definition = TestDefinitions.Create();
        await using var scheduler = CreateScheduler();
        await using var runtime = CreateRuntime(database, definition, scheduler, out _);

        var first = await runtime.CreateAsync(TestDefinitions.WidgetId, settingsJson: """{"slot":1}""");
        var second = await runtime.CreateAsync(TestDefinitions.WidgetId, settingsJson: """{"slot":2}""");

        var store = database.CreateInstanceService();
        var rows = await store.GetByWidgetIdAsync(TestDefinitions.WidgetId);

        Assert.Equal(2, rows.Count);
        Assert.Equal("""{"slot":1}""", (await store.GetAsync(first.Id))!.SettingsJson);
        Assert.Equal("""{"slot":2}""", (await store.GetAsync(second.Id))!.SettingsJson);
    }

    [Fact]
    public async Task UnknownWidgetRowsArePreservedWithUnavailableState()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var store = database.CreateInstanceService();
        var orphan = await store.CreateAsync(new WidgetInstance
        {
            WidgetId = "missing.widget",
            InstanceName = "Orphan",
            IsEnabled = true,
            Width = 260,
            Height = 150,
            SettingsJson = "{}",
            SettingsSchemaVersion = 1
        });

        var definition = TestDefinitions.Create();
        await using var scheduler = CreateScheduler();
        await using var runtime = CreateRuntime(database, definition, scheduler, out _);

        var report = await runtime.StartAllAsync();

        Assert.Equal(1, report.Unavailable);
        Assert.NotNull(await store.GetAsync(orphan.Id));
        Assert.Equal(WidgetInstanceState.Unavailable, runtime.GetInstance(orphan.Id)!.State);
    }

    [Fact]
    public async Task RestartingOpenBarDoesNotDuplicateRows()
    {
        await using var database = new RuntimeDatabase();
        await database.InitializeAsync();

        var definition = TestDefinitions.Create();
        await using (var scheduler = CreateScheduler())
        await using (var runtime = CreateRuntime(database, definition, scheduler, out _))
        {
            _ = await runtime.CreateAsync(TestDefinitions.WidgetId);
        }

        await using (var scheduler = CreateScheduler())
        await using (var runtime = CreateRuntime(database, definition, scheduler, out _))
        {
            _ = await runtime.StartAllAsync();
        }

        var store = database.CreateInstanceService();
        var rows = await store.GetAllAsync();

        Assert.Single(rows);
    }

    private static WidgetScheduler CreateScheduler()
        => new(
            new WidgetSchedulerOptions { MinimumInterval = TimeSpan.FromMilliseconds(10) },
            TimeProvider.System,
            NullLogger<WidgetScheduler>.Instance);

    private static WidgetRuntimeService CreateRuntime(
        RuntimeDatabase database,
        WidgetDefinition definition,
        WidgetScheduler scheduler,
        out FakeWidgetWindowService windows,
        FakeWidgetWindowService? existingWindows = null)
    {
        var registry = new WidgetRegistry();
        registry.Register(definition);
        windows = existingWindows ?? new FakeWidgetWindowService();

        return new WidgetRuntimeService(
            database.CreateInstanceService(),
            registry,
            windows,
            scheduler,
            NullLogger<WidgetRuntimeService>.Instance);
    }
}

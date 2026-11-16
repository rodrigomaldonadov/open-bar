using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Infrastructure.Persistence;
using OpenBar.WidgetRuntime.Tests.Fakes;

namespace OpenBar.WidgetRuntime.Tests;

/// <summary>Harness with the runtime, a fake window host and an instance store.</summary>
internal sealed class RuntimeHarness : IAsyncDisposable
{
    private readonly WidgetScheduler _scheduler;

    private RuntimeHarness(
        WidgetRuntimeService runtime,
        WidgetScheduler scheduler,
        InMemoryWidgetInstanceService store,
        FakeWidgetWindowService windows)
    {
        Runtime = runtime;
        _scheduler = scheduler;
        Store = store;
        Windows = windows;
    }

    public WidgetRuntimeService Runtime { get; }

    public InMemoryWidgetInstanceService Store { get; }

    public FakeWidgetWindowService Windows { get; }

    public static RuntimeHarness Create(params OpenBar.Widgets.Abstractions.WidgetDefinition[] definitions)
    {
        var registry = new WidgetRegistry();
        foreach (var definition in definitions)
        {
            registry.Register(definition);
        }

        var store = new InMemoryWidgetInstanceService();
        var windows = new FakeWidgetWindowService();
        var scheduler = new WidgetScheduler(
            new WidgetSchedulerOptions { MinimumInterval = TimeSpan.FromMilliseconds(10) },
            TimeProvider.System,
            NullLogger<WidgetScheduler>.Instance);

        var runtime = new WidgetRuntimeService(
            store,
            registry,
            windows,
            scheduler,
            NullLogger<WidgetRuntimeService>.Instance);

        return new RuntimeHarness(runtime, scheduler, store, windows);
    }

    public async ValueTask DisposeAsync()
    {
        await Runtime.DisposeAsync();
        await _scheduler.DisposeAsync();
    }
}

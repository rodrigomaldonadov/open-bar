using OpenBar.Domain.Widgets;
using OpenBar.WidgetRuntime.Tests.Fakes;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class WidgetPresentationTests
{
    [Fact]
    public async Task SwitchingToEmbeddedStopsTheWindowLifecycleAndOpensOneSurface()
    {
        var factory = new RecordingWidgetFactory();
        await using var harness = RuntimeHarness.Create(TestDefinitions.Create(factory: factory.Create));
        var info = await harness.Runtime.CreateAsync(TestDefinitions.WidgetId);

        Assert.Single(harness.Windows.OpenWindows);

        await harness.Runtime.SetPresentationAsync(
            info.Id,
            WidgetPresentationMode.DockEmbedded,
            0);

        Assert.Equal(2, factory.Created.Count);
        Assert.Equal(1, factory.Created[0].StopCalls);
        Assert.Equal(1, factory.Created[0].DisposeCalls);
        Assert.Equal(1, factory.Last.StartCalls);
        Assert.Empty(harness.Windows.OpenWindows);
        Assert.Single(harness.Surfaces.OpenSurfaces);
        Assert.Equal(
            WidgetPresentationMode.DockEmbedded,
            harness.Store.Find(info.Id)!.PresentationMode);
    }

    [Fact]
    public async Task SwitchingBackToWindowClosesTheSurfaceWithoutDuplicatingLifecycles()
    {
        var factory = new RecordingWidgetFactory();
        await using var harness = RuntimeHarness.Create(TestDefinitions.Create(factory: factory.Create));
        var info = await harness.Runtime.CreateAsync(TestDefinitions.WidgetId);

        await harness.Runtime.SetPresentationAsync(info.Id, WidgetPresentationMode.DockEmbedded, 0);
        await harness.Runtime.SetPresentationAsync(info.Id, WidgetPresentationMode.FloatingWindow, 0);

        Assert.Equal(3, factory.Created.Count);
        Assert.All(factory.Created, widget => Assert.Equal(1, widget.StartCalls));
        Assert.Empty(harness.Surfaces.OpenSurfaces);
        Assert.Single(harness.Windows.OpenWindows);
        Assert.Equal(
            WidgetPresentationMode.FloatingWindow,
            harness.Store.Find(info.Id)!.PresentationMode);
    }

    [Fact]
    public async Task ReorderingAnEmbeddedSurfaceDoesNotRestartTheWidget()
    {
        var factory = new RecordingWidgetFactory();
        await using var harness = RuntimeHarness.Create(TestDefinitions.Create(factory: factory.Create));
        var info = await harness.Runtime.CreateAsync(TestDefinitions.WidgetId);
        await harness.Runtime.SetPresentationAsync(info.Id, WidgetPresentationMode.DockEmbedded, 0);
        var created = factory.Created.Count;

        await harness.Runtime.SetPresentationAsync(info.Id, WidgetPresentationMode.DockEmbedded, 4);

        Assert.Equal(created, factory.Created.Count);
        Assert.Equal(4, Assert.Single(harness.Surfaces.OpenSurfaces).Order);
        Assert.Equal(4, harness.Store.Find(info.Id)!.PresentationOrder);
    }

    [Fact]
    public async Task RecoveryStartsEmbeddedInstancesWithoutOpeningWindows()
    {
        await using var harness = RuntimeHarness.Create(TestDefinitions.Create());
        var seeded = await harness.Store.CreateAsync(new WidgetInstance
        {
            WidgetId = TestDefinitions.WidgetId,
            InstanceName = "Embedded",
            IsEnabled = true,
            PresentationMode = WidgetPresentationMode.DockEmbedded,
            PresentationOrder = 2,
            SettingsJson = "{}",
            SettingsSchemaVersion = 1
        });

        var report = await harness.Runtime.StartAllAsync();

        Assert.Equal(1, report.Started);
        Assert.Single(harness.Surfaces.OpenSurfaces);
        Assert.Empty(harness.Windows.OpenWindows);
        Assert.Equal(WidgetInstanceState.Running, harness.Runtime.GetInstance(seeded.Id)!.State);
    }

    [Fact]
    public async Task DisablingAnEmbeddedWidgetClosesItsSurface()
    {
        await using var harness = RuntimeHarness.Create(TestDefinitions.Create());
        var info = await harness.Runtime.CreateAsync(TestDefinitions.WidgetId);
        await harness.Runtime.SetPresentationAsync(info.Id, WidgetPresentationMode.DockEmbedded, 0);

        await harness.Runtime.SetEnabledAsync(info.Id, false);

        Assert.Empty(harness.Surfaces.OpenSurfaces);
        Assert.False(harness.Store.Find(info.Id)!.IsEnabled);
        Assert.Equal(WidgetInstanceState.Stopped, harness.Runtime.GetInstance(info.Id)!.State);
    }

    [Fact]
    public async Task StopAllClosesEverySurface()
    {
        await using var harness = RuntimeHarness.Create(TestDefinitions.Create());
        var info = await harness.Runtime.CreateAsync(TestDefinitions.WidgetId);
        await harness.Runtime.SetPresentationAsync(info.Id, WidgetPresentationMode.DockEmbedded, 0);

        await harness.Runtime.StopAllAsync();

        Assert.Empty(harness.Surfaces.OpenSurfaces);
        Assert.Equal(1, harness.Surfaces.CloseAllCalls);
    }
}

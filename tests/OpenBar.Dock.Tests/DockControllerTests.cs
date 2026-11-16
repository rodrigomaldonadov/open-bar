using OpenBar.Application.Dock;
using OpenBar.Domain.Dock;
using OpenBar.Domain.Layouts;
using Xunit;

namespace OpenBar.Dock.Tests;

public sealed class DockControllerTests
{
    [Fact]
    public async Task InitializeCreatesADefaultLayoutAndLoadsPinnedItems()
    {
        using var context = new DockTestContext();

        await context.Controller.InitializeAsync();

        Assert.Single(await context.Layouts.GetAllAsync());
        Assert.Equal(DockEdge.Bottom, context.Controller.Layout.Position);
        Assert.Equal(DockAlignment.Center, context.Controller.Layout.Alignment);
        Assert.Empty(context.Controller.Items);
    }

    [Fact]
    public async Task AddingAnApplicationResolvesAndPersistsIt()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        context.Resolver.Result = new ResolvedApplicationTarget(path, null, "app-aumid");
        await context.Controller.InitializeAsync();

        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(path, "Editor"));

        var item = Assert.Single(context.Controller.Items);
        Assert.True(item.IsPinned);
        Assert.Equal("Editor", item.DisplayName);
        Assert.Equal(path, item.LaunchTarget);
        Assert.Equal("app-aumid", (await context.Pinned.GetAllAsync()).Single().AppUserModelId);
    }

    [Fact]
    public async Task AddingAShortcutUsesTheResolvedExecutableForItsIcon()
    {
        using var context = new DockTestContext();
        var shortcut = context.CreateFile("app.lnk");
        var executable = context.CreateExecutable("resolved.exe");
        context.Resolver.Result = new ResolvedApplicationTarget(executable, null, null);
        await context.Controller.InitializeAsync();

        await context.Controller.AddPinnedAsync(new DiscoveredApplication(
            "Shortcut",
            shortcut,
            PinnedTargetKind.Shortcut,
            IconPath: shortcut));

        Assert.Equal(executable, (await context.Pinned.GetAllAsync()).Single().IconKey);
    }

    [Fact]
    public async Task AddingAMissingTargetIsRejected()
    {
        using var context = new DockTestContext();
        await context.Controller.InitializeAsync();
        var missing = Path.Combine(context.DirectoryPath, "missing.exe");

        await Assert.ThrowsAsync<DockValidationException>(
            () => context.Controller.AddPinnedAsync(DockTestContext.Discovered(missing)));

        Assert.Empty(context.Controller.Items);
    }

    [Fact]
    public async Task AddingTheSameTargetTwiceIsRejected()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        await context.Controller.InitializeAsync();

        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(path));

        await Assert.ThrowsAsync<DockValidationException>(
            () => context.Controller.AddPinnedAsync(DockTestContext.Discovered(path)));
    }

    [Fact]
    public async Task ReorderPersistsTheRequestedOrder()
    {
        using var context = new DockTestContext();
        var firstPath = context.CreateExecutable("first.exe");
        var secondPath = context.CreateExecutable("second.exe");
        await context.Controller.InitializeAsync();
        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(firstPath, "First"));
        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(secondPath, "Second"));

        var ids = context.Controller.Items.Select(item => item.PinnedId!.Value).Reverse().ToArray();
        await context.Controller.ReorderPinnedAsync(ids);

        Assert.Equal(["Second", "First"], context.Controller.Items.Select(item => item.DisplayName));
        Assert.Equal(ids, (await context.Pinned.GetAllAsync()).Select(application => application.Id));
    }

    [Fact]
    public async Task RemoveDeletesThePinnedApplication()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        await context.Controller.InitializeAsync();
        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(path));
        var id = context.Controller.Items.Single().PinnedId!.Value;

        await context.Controller.RemovePinnedAsync(id);

        Assert.Empty(context.Controller.Items);
        Assert.Empty(await context.Pinned.GetAllAsync());
    }

    [Fact]
    public async Task ActivatingAnUnpinnedRunningWindowActivatesIt()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        context.Windows.Windows.Add(new AppWindowSnapshot((IntPtr)123, 42, path, "app-aumid", "App", "App"));
        await context.Controller.InitializeAsync();

        await context.Controller.ActivateAsync(context.Controller.Items.Single());

        Assert.Equal((IntPtr)123, Assert.Single(context.Launcher.Activated));
        Assert.Empty(context.Launcher.Launched);
    }

    [Fact]
    public async Task ActivatingAPinnedApplicationLaunchesItWhenNotRunning()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        await context.Controller.InitializeAsync();
        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(path));

        await context.Controller.ActivateAsync(context.Controller.Items.Single());

        Assert.Single(context.Launcher.Launched);
        Assert.Empty(context.Launcher.Activated);
    }

    [Fact]
    public async Task ActivatingAPinnedRunningApplicationFocusesItsWindow()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        await context.Controller.InitializeAsync();
        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(path));
        context.Windows.Windows.Add(new AppWindowSnapshot((IntPtr)456, 42, path, "app-aumid", "App", "App"));
        await context.Controller.RefreshRunningAsync();

        await context.Controller.ActivateAsync(context.Controller.Items.Single());

        Assert.Equal((IntPtr)456, Assert.Single(context.Launcher.Activated));
        Assert.Empty(context.Launcher.Launched);
    }

    [Fact]
    public async Task LaunchCooldownSuppressesAccidentalDuplicates()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        await context.Controller.InitializeAsync();
        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(path));
        var item = context.Controller.Items.Single();

        await context.Controller.ActivateAsync(item);
        await context.Controller.ActivateAsync(item);
        Assert.Single(context.Launcher.Launched);

        context.Time.Advance(TimeSpan.FromSeconds(2));
        await context.Controller.ActivateAsync(item);

        Assert.Equal(2, context.Launcher.Launched.Count);
    }

    [Fact]
    public async Task RefreshRunningMergesNewWindowsWithoutLosingPinnedItems()
    {
        using var context = new DockTestContext();
        var path = context.CreateExecutable();
        await context.Controller.InitializeAsync();
        await context.Controller.AddPinnedAsync(DockTestContext.Discovered(path, "Pinned"));

        context.Windows.Windows.Add(new AppWindowSnapshot((IntPtr)789, 7, @"C:\Apps\other.exe", "other", "Other", "Other"));
        await context.Controller.RefreshRunningAsync();

        Assert.Equal(2, context.Controller.Items.Count);
        Assert.True(context.Controller.Items[0].IsPinned);
        Assert.False(context.Controller.Items[1].IsPinned);
        Assert.Equal("Other", context.Controller.Items[1].DisplayName);
    }

    [Fact]
    public async Task UpdateLayoutClampsAndPersistsTheValues()
    {
        using var context = new DockTestContext();
        await context.Controller.InitializeAsync();
        var layout = context.Controller.Layout;
        layout.IconSize = 500;
        layout.Spacing = -10;
        layout.Magnification = 9;
        layout.MaterialOpacity = 9;
        layout.AnimationDurationMs = 9_999;
        layout.AutoHide = true;
        layout.CornerRadius = 999;
        layout.AutoHideShowDelayMs = -1;
        layout.AutoHideHideDelayMs = 99_999;
        layout.Theme = (DockTheme)99;
        layout.ReduceAnimations = true;
        layout.HideInFullscreen = false;

        await context.Controller.UpdateLayoutAsync(layout);

        Assert.Equal(96, context.Controller.Layout.IconSize);
        Assert.Equal(0, context.Controller.Layout.Spacing);
        Assert.Equal(1.5, context.Controller.Layout.Magnification);
        Assert.Equal(1, context.Controller.Layout.MaterialOpacity);
        Assert.Equal(1_000, context.Controller.Layout.AnimationDurationMs);
        Assert.True(context.Controller.Layout.AutoHide);
        Assert.Equal(48, context.Controller.Layout.CornerRadius);
        Assert.Equal(0, context.Controller.Layout.AutoHideShowDelayMs);
        Assert.Equal(5_000, context.Controller.Layout.AutoHideHideDelayMs);
        Assert.Equal(DockTheme.System, context.Controller.Layout.Theme);
        Assert.True(context.Controller.Layout.ReduceAnimations);
        Assert.False(context.Controller.Layout.HideInFullscreen);
        Assert.Equal(96 + 24, context.Controller.Layout.Size);
    }

    [Fact]
    public async Task DiscoverApplicationsDelegatesToTheCatalog()
    {
        using var context = new DockTestContext();
        context.Catalog.Applications.Add(new DiscoveredApplication("App", @"C:\Apps\app.exe", PinnedTargetKind.Executable));
        await context.Controller.InitializeAsync();

        var discovered = await context.Controller.DiscoverApplicationsAsync();

        Assert.Single(discovered);
        Assert.Equal("App", discovered[0].Name);
    }
}

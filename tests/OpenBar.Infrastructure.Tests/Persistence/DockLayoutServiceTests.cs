using OpenBar.Application.Persistence;
using OpenBar.Domain.Layouts;
using Xunit;

namespace OpenBar.Infrastructure.Tests.Persistence;

public sealed class DockLayoutServiceTests
{
    [Fact]
    public async Task CreatePersistsEveryField()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        var created = await service.CreateAsync(new DockLayout
        {
            Name = "Work",
            Position = DockEdge.Left,
            Alignment = DockAlignment.Stretch,
            Size = 64,
            Offset = 12,
            AutoHide = true,
            IsDefault = true,
            MonitorTarget = "index:1"
        });

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(default, created.CreatedAt);

        var reloaded = await service.GetAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Work", reloaded.Name);
        Assert.Equal(DockEdge.Left, reloaded.Position);
        Assert.Equal(DockAlignment.Stretch, reloaded.Alignment);
        Assert.Equal(64, reloaded.Size);
        Assert.Equal(12, reloaded.Offset);
        Assert.True(reloaded.AutoHide);
        Assert.True(reloaded.IsDefault);
        Assert.Equal("index:1", reloaded.MonitorTarget);
    }

    [Fact]
    public async Task GetDefaultReturnsTheMarkedLayout()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        _ = await service.CreateAsync(Layout("Secondary"));
        var primary = await service.CreateAsync(Layout("Primary", isDefault: true));

        var defaultLayout = await service.GetDefaultAsync();

        Assert.NotNull(defaultLayout);
        Assert.Equal(primary.Id, defaultLayout.Id);
    }

    [Fact]
    public async Task CreatingASecondDefaultClearsThePreviousOne()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        var first = await service.CreateAsync(Layout("First", isDefault: true));
        var second = await service.CreateAsync(Layout("Second", isDefault: true));

        var all = await service.GetAllAsync();
        Assert.Equal(2, all.Count);
        Assert.False(all.Single(layout => layout.Id == first.Id).IsDefault);
        Assert.True(all.Single(layout => layout.Id == second.Id).IsDefault);

        var defaultLayout = await service.GetDefaultAsync();
        Assert.Equal(second.Id, defaultLayout!.Id);
    }

    [Fact]
    public async Task SetDefaultMovesTheDefaultAtomically()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        var first = await service.CreateAsync(Layout("First", isDefault: true));
        var second = await service.CreateAsync(Layout("Second"));

        await service.SetDefaultAsync(second.Id);

        Assert.False((await service.GetAsync(first.Id))!.IsDefault);
        Assert.True((await service.GetAsync(second.Id))!.IsDefault);
        Assert.Equal(1, (await service.GetAllAsync()).Count(layout => layout.IsDefault));
    }

    [Fact]
    public async Task SetDefaultOnMissingLayoutThrowsNotFound()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();

        await Assert.ThrowsAsync<PersistenceNotFoundException>(
            () => service.SetDefaultAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task DuplicateNameThrowsConflict()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        _ = await service.CreateAsync(Layout("Work"));

        await Assert.ThrowsAsync<PersistenceConflictException>(
            () => service.CreateAsync(Layout("Work")));
    }

    [Fact]
    public async Task UpdateChangesFieldsAndCanBecomeDefault()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        var existingDefault = await service.CreateAsync(Layout("Old default", isDefault: true));
        var target = await service.CreateAsync(Layout("Target"));

        target.Name = "Renamed";
        target.Position = DockEdge.Top;
        target.Alignment = DockAlignment.End;
        target.Size = 88;
        target.Offset = 4;
        target.AutoHide = true;
        target.IsDefault = true;

        var updated = await service.UpdateAsync(target);
        Assert.Equal("Renamed", updated.Name);
        Assert.Equal(DockEdge.Top, updated.Position);
        Assert.Equal(88, updated.Size);

        Assert.False((await service.GetAsync(existingDefault.Id))!.IsDefault);
        Assert.True((await service.GetAsync(target.Id))!.IsDefault);
    }

    [Fact]
    public async Task DeleteRemovesLayout()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        var created = await service.CreateAsync(Layout("Work"));

        Assert.True(await service.DeleteAsync(created.Id));
        Assert.False(await service.DeleteAsync(created.Id));
        Assert.Null(await service.GetAsync(created.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsEmptyName(string name)
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(Layout(name)));
    }

    [Fact]
    public async Task RejectsInvalidGeometryAndMonitorTarget()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();

        var zeroSize = Layout("Size");
        zeroSize.Size = 0;
        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(zeroSize));

        var negativeOffset = Layout("Offset");
        negativeOffset.Offset = -1;
        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(negativeOffset));

        var invalidPosition = Layout("Position");
        invalidPosition.Position = (DockEdge)99;
        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(invalidPosition));

        var emptyMonitor = Layout("Monitor");
        emptyMonitor.MonitorTarget = " ";
        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(emptyMonitor));
    }

    [Fact]
    public async Task MonitorTargetKeepsLogicalSelectorsInsteadOfHardwareIds()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateDockLayoutService();
        _ = await service.CreateAsync(Layout("Primary", monitorTarget: "primary"));
        _ = await service.CreateAsync(Layout("Second", monitorTarget: "index:2"));

        var all = await service.GetAllAsync();

        Assert.Contains(all, layout => layout.MonitorTarget == "primary");
        Assert.Contains(all, layout => layout.MonitorTarget == "index:2");
    }

    [Fact]
    public async Task LayoutsSurviveClosingAndReopeningTheContextFactory()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var first = database.CreateDockLayoutService();
        var created = await first.CreateAsync(Layout("Persisted", isDefault: true));

        var second = database.CreateDockLayoutService();
        var defaultLayout = await second.GetDefaultAsync();

        Assert.NotNull(defaultLayout);
        Assert.Equal(created.Id, defaultLayout.Id);
        Assert.Equal("Persisted", defaultLayout.Name);
    }

    private static DockLayout Layout(
        string name,
        bool isDefault = false,
        string monitorTarget = DockLayout.PrimaryMonitorTarget)
        => new()
        {
            Name = name,
            Position = DockEdge.Bottom,
            Alignment = DockAlignment.Center,
            Size = 72,
            Offset = 0,
            IsDefault = isDefault,
            MonitorTarget = monitorTarget
        };

    private static async Task InitializeAsync(PersistenceTestContext database)
    {
        using var initializer = database.CreateInitializer();
        _ = await initializer.InitializeAsync();
    }
}

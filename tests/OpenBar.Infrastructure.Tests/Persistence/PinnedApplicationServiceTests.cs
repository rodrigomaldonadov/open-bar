using OpenBar.Application.Persistence;
using OpenBar.Domain.Dock;
using Xunit;

namespace OpenBar.Infrastructure.Tests.Persistence;

public sealed class PinnedApplicationServiceTests
{
    [Fact]
    public async Task AddAndGetAllReturnsApplicationsInSortOrder()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreatePinnedApplicationService();
        _ = await service.AddAsync(New("Second", @"C:\Apps\second.exe", 1));
        _ = await service.AddAsync(New("First", @"C:\Apps\first.exe", 0));

        var all = await service.GetAllAsync();

        Assert.Equal(["First", "Second"], all.Select(application => application.Name));
        Assert.Equal(PinnedTargetKind.Shortcut, all[0].TargetKind);
        Assert.NotEqual(default, all[0].CreatedAt);
    }

    [Fact]
    public async Task DuplicateTargetThrowsConflict()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreatePinnedApplicationService();
        _ = await service.AddAsync(New("One", @"C:\Apps\same.exe", 0));

        await Assert.ThrowsAsync<PersistenceConflictException>(
            () => service.AddAsync(New("Two", @"C:\Apps\same.exe", 1)));
    }

    [Fact]
    public async Task ReorderPersistsEveryPosition()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreatePinnedApplicationService();
        var first = await service.AddAsync(New("First", @"C:\Apps\first.exe", 0));
        var second = await service.AddAsync(New("Second", @"C:\Apps\second.exe", 1));
        var third = await service.AddAsync(New("Third", @"C:\Apps\third.exe", 2));

        await service.ReorderAsync([third.Id, first.Id, second.Id]);

        var ordered = await service.GetAllAsync();
        Assert.Equal(["Third", "First", "Second"], ordered.Select(application => application.Name));
        Assert.Equal([0, 1, 2], ordered.Select(application => application.SortOrder));
    }

    [Fact]
    public async Task ReorderRejectsAnIncompleteIdSet()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreatePinnedApplicationService();
        var first = await service.AddAsync(New("First", @"C:\Apps\first.exe", 0));
        _ = await service.AddAsync(New("Second", @"C:\Apps\second.exe", 1));

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.ReorderAsync([first.Id]));
    }

    [Fact]
    public async Task RemoveDeletesTheApplication()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreatePinnedApplicationService();
        var application = await service.AddAsync(New("One", @"C:\Apps\one.exe", 0));

        Assert.True(await service.RemoveAsync(application.Id));
        Assert.False(await service.RemoveAsync(application.Id));
        Assert.Empty(await service.GetAllAsync());
    }

    [Fact]
    public async Task ApplicationsSurviveClosingAndReopeningTheContextFactory()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var first = database.CreatePinnedApplicationService();
        var application = await first.AddAsync(New("Persisted", @"C:\Apps\persisted.exe", 4));

        var second = database.CreatePinnedApplicationService();
        var reloaded = Assert.Single(await second.GetAllAsync());

        Assert.Equal(application.Id, reloaded.Id);
        Assert.Equal(4, reloaded.SortOrder);
    }

    [Theory]
    [InlineData("", @"C:\Apps\one.exe")]
    [InlineData("   ", @"C:\Apps\one.exe")]
    [InlineData("Name", "")]
    public async Task InvalidNameOrTargetIsRejected(string name, string target)
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreatePinnedApplicationService();

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.AddAsync(New(name, target, 0)));
    }

    private static PinnedApplication New(string name, string target, int sortOrder)
        => new()
        {
            Name = name,
            Target = target,
            TargetKind = PinnedTargetKind.Shortcut,
            SortOrder = sortOrder
        };

    private static async Task InitializeAsync(PersistenceTestContext database)
    {
        using var initializer = database.CreateInitializer();
        _ = await initializer.InitializeAsync();
    }
}

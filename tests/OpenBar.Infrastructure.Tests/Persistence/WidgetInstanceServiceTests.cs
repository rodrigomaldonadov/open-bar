using OpenBar.Application.Persistence;
using OpenBar.Domain.Widgets;
using Xunit;

namespace OpenBar.Infrastructure.Tests.Persistence;

public sealed class WidgetInstanceServiceTests
{
    [Fact]
    public async Task CreateAssignsIdAndTimestamps()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();
        var created = await service.CreateAsync(NewInstance("clock", "Desk clock"));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
        Assert.True(created.IsEnabled);
        Assert.Equal("{}", created.SettingsJson);

        var reloaded = await service.GetAsync(created.Id);
        Assert.NotNull(reloaded);
        Assert.Equal("Desk clock", reloaded.InstanceName);
    }

    [Fact]
    public async Task SupportsMultipleInstancesOfTheSameWidgetWithIndependentJson()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();

        var tokyo = await service.CreateAsync(NewInstance("clock", "Tokyo", """{"zone":"Asia/Tokyo"}"""));
        var madrid = await service.CreateAsync(NewInstance("clock", "Madrid", """{"zone":"Europe/Madrid"}"""));

        var instances = await service.GetByWidgetIdAsync("clock");

        Assert.Equal(2, instances.Count);
        Assert.NotEqual(tokyo.Id, madrid.Id);
        Assert.Equal("""{"zone":"Asia/Tokyo"}""", (await service.GetAsync(tokyo.Id))!.SettingsJson);
        Assert.Equal("""{"zone":"Europe/Madrid"}""", (await service.GetAsync(madrid.Id))!.SettingsJson);
    }

    [Fact]
    public async Task SameInstanceNameIsRejectedPerWidgetTypeButAllowedAcrossTypes()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();
        _ = await service.CreateAsync(NewInstance("clock", "Main"));

        await Assert.ThrowsAsync<PersistenceConflictException>(
            () => service.CreateAsync(NewInstance("clock", "Main")));

        var otherType = await service.CreateAsync(NewInstance("weather", "Main"));
        Assert.Equal("weather", otherType.WidgetId);
    }

    [Fact]
    public async Task UpdateSettingsChangesOnlyJsonAndSchemaVersion()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();
        var created = await service.CreateAsync(NewInstance("clock", "Main"));

        var updated = await service.UpdateSettingsAsync(
            created.Id,
            """{"zone":"UTC","format":"24h"}""",
            2);

        Assert.Equal(created.InstanceName, updated.InstanceName);
        Assert.Equal(created.WidgetId, updated.WidgetId);
        Assert.Equal(2, updated.SettingsSchemaVersion);
        Assert.Equal("""{"zone":"UTC","format":"24h"}""", updated.SettingsJson);

        var reloaded = await service.GetAsync(created.Id);
        Assert.Equal(2, reloaded!.SettingsSchemaVersion);
        Assert.Equal("""{"zone":"UTC","format":"24h"}""", reloaded.SettingsJson);
    }

    [Fact]
    public async Task GetEnabledExcludesDisabledInstances()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();
        var enabled = await service.CreateAsync(NewInstance("clock", "Enabled"));
        var disabled = await service.CreateAsync(NewInstance("clock", "Disabled"));
        _ = await service.SetEnabledAsync(disabled.Id, false);

        var result = await service.GetEnabledAsync();

        var only = Assert.Single(result);
        Assert.Equal(enabled.Id, only.Id);
        Assert.False((await service.GetAsync(disabled.Id))!.IsEnabled);
    }

    [Fact]
    public async Task SaveManyRollsBackTheWholeBatchWhenOneRowViolatesAUniqueIndex()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();
        _ = await service.CreateAsync(NewInstance("clock", "Existing"));

        var candidate = NewInstance("clock", "Brand new");
        var conflicting = NewInstance("clock", "Existing");

        await Assert.ThrowsAsync<PersistenceConflictException>(
            () => service.SaveManyAsync([candidate, conflicting]));

        // The batch is transactional: the valid insert must not survive a
        // failure caused by its sibling row.
        var instances = await service.GetByWidgetIdAsync("clock");
        Assert.Single(instances);
        Assert.Equal("Existing", instances[0].InstanceName);
    }

    [Fact]
    public async Task SaveManyRejectsDuplicateIdsInsideTheBatch()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();

        var id = Guid.NewGuid();
        var first = NewInstance("clock", "One");
        first.Id = id;
        var second = NewInstance("clock", "Two");
        second.Id = id;

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.SaveManyAsync([first, second]));
    }

    [Fact]
    public async Task SaveManyInsertsAndUpdatesTogether()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();
        var existing = await service.CreateAsync(NewInstance("clock", "Main"));

        existing.PositionX = 10;
        existing.PositionY = 20;
        existing.Width = 320;
        existing.Height = 160;

        await service.SaveManyAsync([existing, NewInstance("weather", "Forecast")]);

        var reloaded = await service.GetAsync(existing.Id);
        Assert.Equal(10, reloaded!.PositionX);
        Assert.Equal(20, reloaded.PositionY);
        Assert.Equal(320, reloaded.Width);
        Assert.Equal(160, reloaded.Height);

        Assert.Equal(2, (await service.GetAllAsync()).Count);
    }

    [Fact]
    public async Task RejectsInvalidSettingsJsonAndSchemaVersion()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();

        var invalidJson = NewInstance("clock", "Main");
        invalidJson.SettingsJson = "{ not json";

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(invalidJson));

        var invalidVersion = NewInstance("clock", "Main");
        invalidVersion.SettingsSchemaVersion = 0;

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(invalidVersion));
    }

    [Fact]
    public async Task RejectsInvalidGeometryAndIdentity()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();

        var missingWidgetId = NewInstance(" ", "Main");
        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(missingWidgetId));

        var missingName = NewInstance("clock", " ");
        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(missingName));

        var negativeWidth = NewInstance("clock", "Main");
        negativeWidth.Width = -1;
        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => service.CreateAsync(negativeWidth));
    }

    [Fact]
    public async Task DeleteRemovesInstance()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var service = database.CreateWidgetInstanceService();
        var created = await service.CreateAsync(NewInstance("clock", "Main"));

        Assert.True(await service.DeleteAsync(created.Id));
        Assert.False(await service.DeleteAsync(created.Id));
        Assert.Null(await service.GetAsync(created.Id));
    }

    [Fact]
    public async Task InstancesSurviveClosingAndReopeningTheContextFactory()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var first = database.CreateWidgetInstanceService();
        var created = await first.CreateAsync(NewInstance("clock", "Main", """{"zone":"UTC"}"""));

        var second = database.CreateWidgetInstanceService();
        var reloaded = await second.GetAsync(created.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("""{"zone":"UTC"}""", reloaded.SettingsJson);
    }

    private static WidgetInstance NewInstance(string widgetId, string instanceName, string settingsJson = "{}")
        => new()
        {
            WidgetId = widgetId,
            InstanceName = instanceName,
            SettingsJson = settingsJson
        };

    private static async Task InitializeAsync(PersistenceTestContext database)
    {
        using var initializer = database.CreateInitializer();
        _ = await initializer.InitializeAsync();
    }
}

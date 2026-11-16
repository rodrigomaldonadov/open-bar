using OpenBar.Application.Persistence;
using Xunit;

namespace OpenBar.Infrastructure.Tests.Persistence;

public sealed class AppSettingsServiceTests
{
    [Fact]
    public async Task CreateThenReadRoundTripsValueAndTimestamps()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();
        var created = await settings.CreateAsync("ui.theme", "dark");

        Assert.Equal("ui.theme", created.Key);
        Assert.Equal("dark", created.Value);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);

        var reloaded = await settings.GetAsync("ui.theme");
        Assert.NotNull(reloaded);
        Assert.Equal("dark", reloaded.Value);
        Assert.Equal("dark", await settings.GetValueAsync("ui.theme"));
    }

    [Fact]
    public async Task GetAllReturnsSettingsOrderedByKey()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();
        await settings.CreateAsync("zeta", "1");
        await settings.CreateAsync("alpha", "2");
        await settings.CreateAsync("middle", "3");

        var all = await settings.GetAllAsync();

        Assert.Equal(["alpha", "middle", "zeta"], all.Select(setting => setting.Key));
    }

    [Fact]
    public async Task CreateDuplicateKeyThrowsConflict()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();
        _ = await settings.CreateAsync("ui.theme", "dark");

        await Assert.ThrowsAsync<PersistenceConflictException>(
            () => settings.CreateAsync("ui.theme", "light"));
    }

    [Fact]
    public async Task UpdateMissingSettingThrowsNotFound()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();

        await Assert.ThrowsAsync<PersistenceNotFoundException>(
            () => settings.UpdateAsync("missing", "value"));
    }

    [Fact]
    public async Task SetInsertsWhenMissingAndUpdatesWhenPresent()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();

        var inserted = await settings.SetAsync("dock.autoHide", "true");
        Assert.Equal("true", inserted.Value);

        var updated = await settings.SetAsync("dock.autoHide", "false");
        Assert.Equal("false", updated.Value);
        Assert.Equal(inserted.CreatedAt, updated.CreatedAt);

        Assert.Equal("false", await settings.GetValueAsync("dock.autoHide"));
        Assert.Single(await settings.GetAllAsync());
    }

    [Fact]
    public async Task SetManyWritesNewAndExistingSettingsInOnePass()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();
        var original = await settings.CreateAsync("ui.theme", "dark");

        await settings.SetManyAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui.theme"] = "light",
            ["ui.language"] = "es-ES",
            ["dock.autoHide"] = "true"
        });

        var all = await settings.GetAllAsync();
        Assert.Equal(3, all.Count);

        var theme = Assert.Single(all, setting => setting.Key == "ui.theme");
        Assert.Equal("light", theme.Value);
        Assert.Equal(original.CreatedAt, theme.CreatedAt);
        Assert.True(theme.UpdatedAt >= theme.CreatedAt);

        Assert.Equal("es-ES", await settings.GetValueAsync("ui.language"));
        Assert.Equal("true", await settings.GetValueAsync("dock.autoHide"));
    }

    [Fact]
    public async Task SetManyRejectsSecretKeysWithoutWritingAnything()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => settings.SetManyAsync(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["ui.theme"] = "dark",
                ["integration.github.token"] = "should-never-be-stored"
            }));

        Assert.Empty(await settings.GetAllAsync());
    }

    [Fact]
    public async Task DeleteRemovesSettingAndReportsWhetherAnythingWasDeleted()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();
        _ = await settings.CreateAsync("ui.theme", "dark");

        Assert.True(await settings.DeleteAsync("ui.theme"));
        Assert.False(await settings.DeleteAsync("ui.theme"));
        Assert.Null(await settings.GetAsync("ui.theme"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RejectsEmptyKeys(string key)
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => settings.CreateAsync(key, "value"));
    }

    [Fact]
    public async Task RejectsOverlongKeyAndValue()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => settings.CreateAsync(new string('k', 201), "value"));

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => settings.CreateAsync("ui.blob", new string('v', 32_769)));
    }

    [Theory]
    [InlineData("integration.password")]
    [InlineData("integration.apiKey")]
    [InlineData("integration.api_key")]
    [InlineData("oauth.token")]
    [InlineData("service.secret")]
    [InlineData("service.private_key")]
    public async Task RejectsObviousCredentialKeys(string key)
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var settings = database.CreateAppSettingsService();

        await Assert.ThrowsAsync<PersistenceValidationException>(
            () => settings.SetAsync(key, "value"));
    }

    [Fact]
    public async Task SettingsSurviveClosingAndReopeningTheContextFactory()
    {
        await using var database = new PersistenceTestContext();
        await InitializeAsync(database);

        var first = database.CreateAppSettingsService();
        _ = await first.CreateAsync("ui.theme", "dark");

        // A brand-new service instance creates fresh contexts over the same file.
        var second = database.CreateAppSettingsService();
        Assert.Equal("dark", await second.GetValueAsync("ui.theme"));
    }

    private static async Task InitializeAsync(PersistenceTestContext database)
    {
        using var initializer = database.CreateInitializer();
        _ = await initializer.InitializeAsync();
    }
}

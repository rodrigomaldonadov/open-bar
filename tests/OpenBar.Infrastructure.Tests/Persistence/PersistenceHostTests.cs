using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Persistence;
using OpenBar.Infrastructure.Persistence;
using Xunit;

namespace OpenBar.Infrastructure.Tests.Persistence;

/// <summary>
/// Verifies the generic-host integration: persistence initializes on start,
/// settings are loaded into the cache, and stopping the host releases every
/// SQLite handle.
/// </summary>
public sealed class PersistenceHostTests
{
    [Fact]
    public async Task HostStartCreatesTheDatabaseAndExposesPersistenceServices()
    {
        await using var database = new PersistenceTestContext();
        using var host = BuildHost(database.Options);

        await host.StartAsync();

        Assert.True(File.Exists(database.Options.DatabasePath));
        Assert.StartsWith(
            Path.GetFullPath(database.DirectoryPath),
            Path.GetFullPath(database.Options.DatabasePath),
            StringComparison.OrdinalIgnoreCase);

        Assert.NotNull(host.Services.GetRequiredService<IDatabaseInitializer>());
        Assert.NotNull(host.Services.GetRequiredService<IAppSettingsService>());
        Assert.NotNull(host.Services.GetRequiredService<IWidgetInstanceService>());
        Assert.NotNull(host.Services.GetRequiredService<IDockLayoutService>());

        await using var context = await database.CreateDbContextAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());

        await host.StopAsync();
    }

    [Fact]
    public async Task HostStartLoadsExistingSettingsIntoTheCache()
    {
        await using var database = new PersistenceTestContext();

        using (var initializer = database.CreateInitializer())
        {
            _ = await initializer.InitializeAsync();
        }

        var settings = database.CreateAppSettingsService();
        await settings.SetManyAsync(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui.theme"] = "dark",
            ["ui.language"] = "es-ES"
        });

        using var host = BuildHost(database.Options);
        await host.StartAsync();

        var cache = host.Services.GetRequiredService<PersistedConfigurationCache>();

        Assert.Equal("dark", cache.Values["ui.theme"]);
        Assert.Equal("es-ES", cache.Values["ui.language"]);
        Assert.NotNull(cache.LoadedAtUtc);

        await host.StopAsync();
    }

    [Fact]
    public async Task HostStartIsIdempotentWhenTheDatabaseIsAlreadyCurrent()
    {
        await using var database = new PersistenceTestContext();

        using (var host = BuildHost(database.Options))
        {
            await host.StartAsync();
            await host.StopAsync();
        }

        using var secondHost = BuildHost(database.Options);
        await secondHost.StartAsync();
        await secondHost.StopAsync();

        await using var context = await database.CreateDbContextAsync();
        Assert.Equal(4, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task HostStopReleasesPooledConnectionsSoTheDatabaseFileCanBeRemoved()
    {
        await using var database = new PersistenceTestContext();
        var options = new OpenBarDatabaseOptions
        {
            DirectoryPath = database.DirectoryPath,
            Pooling = true
        };

        using (var host = BuildHost(options))
        {
            await host.StartAsync();

            var settings = host.Services.GetRequiredService<IAppSettingsService>();
            _ = await settings.SetAsync("ui.theme", "dark");

            await host.StopAsync();
        }

        // Pooling is enabled for this test, so a successful delete proves the
        // hosted service cleared the pool and released the file handles.
        await DeleteDirectoryWithRetriesAsync(database.DirectoryPath);
        Assert.False(Directory.Exists(database.DirectoryPath));
    }

    [Fact]
    public async Task HostStartQuarantinesACorruptDatabaseAndKeepsRunning()
    {
        await using var database = new PersistenceTestContext();

        // A directory path that exists as a file cannot host a database.
        Directory.CreateDirectory(database.DirectoryPath);
        var blockingFile = Path.Combine(database.DirectoryPath, "openbar.db");
        await File.WriteAllTextAsync(blockingFile, "this is not a sqlite database");

        using var host = BuildHost(database.Options);

        // Corruption is quarantined and recovers, so the host must start and
        // preserve the original bytes instead of deleting them.
        await host.StartAsync();

        Assert.True(File.Exists(database.Options.DatabasePath));
        var corrupt = Directory.EnumerateFiles(
            database.Options.CorruptDirectoryPath,
            "*.corrupt",
            SearchOption.TopDirectoryOnly).ToArray();
        Assert.Single(corrupt);

        await host.StopAsync();
    }

    private static IHost BuildHost(OpenBarDatabaseOptions options)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenBarPersistence(options);
        return builder.Build();
    }

    private static async Task DeleteDirectoryWithRetriesAsync(string directoryPath)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(directoryPath))
                {
                    Directory.Delete(directoryPath, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("The test database directory is still locked after host shutdown.");
    }
}

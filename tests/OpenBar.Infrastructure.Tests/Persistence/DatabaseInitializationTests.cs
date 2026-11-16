using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace OpenBar.Infrastructure.Tests.Persistence;

public sealed class DatabaseInitializationTests
{
    [Fact]
    public async Task CreatesDatabaseAndAppliesInitialMigration()
    {
        await using var database = new PersistenceTestContext();
        using var initializer = database.CreateInitializer();

        var result = await initializer.InitializeAsync();

        Assert.True(result.DatabaseCreated);
        Assert.True(File.Exists(database.Options.DatabasePath));
        Assert.Contains(
            result.AppliedMigrations,
            migration => migration.EndsWith("InitialPersistence", StringComparison.Ordinal));
        Assert.Null(result.BackupPath);
        Assert.Null(result.QuarantinedDatabasePath);

        await using var context = await database.CreateDbContextAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task SecondInitializationIsIdempotentAndKeepsTheDatabase()
    {
        await using var database = new PersistenceTestContext();

        using (var first = database.CreateInitializer())
        {
            var firstResult = await first.InitializeAsync();
            Assert.True(firstResult.DatabaseCreated);
            Assert.NotEmpty(firstResult.AppliedMigrations);
        }

        using (var second = database.CreateInitializer())
        {
            var secondResult = await second.InitializeAsync();

            Assert.False(secondResult.DatabaseCreated);
            Assert.Empty(secondResult.AppliedMigrations);
            Assert.Null(secondResult.BackupPath);
        }
    }

    [Fact]
    public async Task CreatesExpectedSchemaIndexesAndWalMode()
    {
        await using var database = new PersistenceTestContext();
        using var initializer = database.CreateInitializer();
        _ = await initializer.InitializeAsync();

        await using var context = await database.CreateDbContextAsync();
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        var tables = await QueryStringsAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'table' ORDER BY name;");

        Assert.Contains("AppSettings", tables);
        Assert.Contains("WidgetInstances", tables);
        Assert.Contains("DockLayouts", tables);
        Assert.Contains("PinnedApplications", tables);
        Assert.Contains("__EFMigrationsHistory", tables);

        var widgetColumns = await QueryStringsAsync(connection, "PRAGMA table_info(\"WidgetInstances\");", column: 1);
        Assert.Contains("Id", widgetColumns);
        Assert.Contains("WidgetId", widgetColumns);
        Assert.Contains("InstanceName", widgetColumns);
        Assert.Contains("IsEnabled", widgetColumns);
        Assert.Contains("PositionX", widgetColumns);
        Assert.Contains("PositionY", widgetColumns);
        Assert.Contains("Width", widgetColumns);
        Assert.Contains("Height", widgetColumns);
        Assert.Contains("SettingsJson", widgetColumns);
        Assert.Contains("SettingsSchemaVersion", widgetColumns);
        Assert.Contains("CreatedAt", widgetColumns);
        Assert.Contains("UpdatedAt", widgetColumns);

        var layoutColumns = await QueryStringsAsync(connection, "PRAGMA table_info(\"DockLayouts\");", column: 1);
        Assert.Contains("MonitorTarget", layoutColumns);
        Assert.Contains("IsDefault", layoutColumns);
        Assert.Contains("AutoHide", layoutColumns);
        Assert.Contains("IconSize", layoutColumns);
        Assert.Contains("Spacing", layoutColumns);
        Assert.Contains("Magnification", layoutColumns);
        Assert.Contains("MaterialOpacity", layoutColumns);
        Assert.Contains("AnimationDurationMs", layoutColumns);

        var pinnedColumns = await QueryStringsAsync(connection, "PRAGMA table_info(\"PinnedApplications\");", column: 1);
        Assert.Contains("Id", pinnedColumns);
        Assert.Contains("Name", pinnedColumns);
        Assert.Contains("Target", pinnedColumns);
        Assert.Contains("TargetKind", pinnedColumns);
        Assert.Contains("ResolvedTarget", pinnedColumns);
        Assert.Contains("AppUserModelId", pinnedColumns);
        Assert.Contains("SortOrder", pinnedColumns);

        var indexes = await QueryStringsAsync(
            connection,
            "SELECT name FROM sqlite_master WHERE type = 'index' AND name NOT LIKE 'sqlite_%';");

        Assert.Contains("UX_WidgetInstances_WidgetId_InstanceName", indexes);
        Assert.Contains("IX_WidgetInstances_IsEnabled", indexes);
        Assert.Contains("UX_DockLayouts_IsDefault", indexes);
        Assert.Contains("UX_DockLayouts_Name", indexes);
        Assert.Contains("UX_PinnedApplications_Target", indexes);
        Assert.Contains("IX_PinnedApplications_SortOrder", indexes);

        var journalMode = await ScalarAsync(connection, "PRAGMA journal_mode;");
        Assert.Equal("wal", journalMode, ignoreCase: true);
    }

    [Fact]
    public async Task CreatesSafetyBackupBeforeApplyingMigrationsToAnExistingFile()
    {
        await using var database = new PersistenceTestContext();
        Directory.CreateDirectory(database.DirectoryPath);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.Options.DatabasePath,
            Pooling = false
        }.ToString();

        await using (var connection = new SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE LegacyProbe (Id INTEGER PRIMARY KEY);";
            await command.ExecuteNonQueryAsync();
        }

        using var initializer = database.CreateInitializer();
        var result = await initializer.InitializeAsync();

        Assert.False(result.DatabaseCreated);
        Assert.NotEmpty(result.AppliedMigrations);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = result.BackupPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        await backup.OpenAsync();
        var backupTables = await QueryStringsAsync(
            backup,
            "SELECT name FROM sqlite_master WHERE type = 'table';");
        Assert.Contains("LegacyProbe", backupTables);
    }

    [Fact]
    public async Task PreservesAndQuarantinesACorruptDatabaseWithoutDeletingIt()
    {
        await using var database = new PersistenceTestContext();
        Directory.CreateDirectory(database.DirectoryPath);

        var corruptBytes = "this is not a sqlite database"u8.ToArray();
        await File.WriteAllBytesAsync(database.Options.DatabasePath, corruptBytes);

        using var initializer = database.CreateInitializer();
        var result = await initializer.InitializeAsync();

        Assert.True(result.DatabaseCreated);
        Assert.NotNull(result.QuarantinedDatabasePath);
        Assert.True(File.Exists(result.QuarantinedDatabasePath));
        Assert.Equal(corruptBytes, await File.ReadAllBytesAsync(result.QuarantinedDatabasePath));

        await using var context = await database.CreateDbContextAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task KeepsExistingDataWhenTheDatabaseAlreadyExists()
    {
        await using var database = new PersistenceTestContext();

        using (var initializer = database.CreateInitializer())
        {
            _ = await initializer.InitializeAsync();
        }

        var settings = database.CreateAppSettingsService();
        _ = await settings.CreateAsync("ui.theme", "dark");

        using (var second = database.CreateInitializer())
        {
            var result = await second.InitializeAsync();
            Assert.False(result.DatabaseCreated);
            Assert.Empty(result.AppliedMigrations);
        }

        var reopened = database.CreateAppSettingsService();
        Assert.Equal("dark", await reopened.GetValueAsync("ui.theme"));
    }

    [Fact]
    public async Task SerializesConcurrentInitialization()
    {
        await using var database = new PersistenceTestContext();
        using var first = database.CreateInitializer();
        using var second = database.CreateInitializer();

        var results = await Task.WhenAll(first.InitializeAsync(), second.InitializeAsync());

        Assert.Contains(results, result => result.DatabaseCreated);
        Assert.Equal(2, results.Sum(result => result.AppliedMigrations.Count));

        await using var context = await database.CreateDbContextAsync();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task HonorsCancellationBeforeTouchingTheFileSystem()
    {
        await using var database = new PersistenceTestContext();
        using var initializer = database.CreateInitializer();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => initializer.InitializeAsync(cancellation.Token));

        Assert.False(Directory.Exists(database.DirectoryPath));
    }

    private static async Task<List<string>> QueryStringsAsync(
        DbConnection connection,
        string commandText,
        int column = 0)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(column));
        }

        return values;
    }

    private static async Task<string?> ScalarAsync(DbConnection connection, string commandText)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return await command.ExecuteScalarAsync() as string;
    }
}

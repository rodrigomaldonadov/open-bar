using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Infrastructure.Persistence;

namespace OpenBar.WidgetRuntime.Tests;

/// <summary>
/// Isolated real SQLite database. Pooling is disabled so the temporary folder
/// can be deleted deterministically.
/// </summary>
internal sealed class RuntimeDatabase : IAsyncDisposable
{
    public RuntimeDatabase()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "OpenBar.WidgetRuntime.Tests",
            Guid.NewGuid().ToString("N"));

        Options = new OpenBarDatabaseOptions
        {
            DirectoryPath = DirectoryPath,
            DatabaseFileName = "openbar.db",
            BusyTimeout = TimeSpan.FromSeconds(5),
            InitializationLockTimeout = TimeSpan.FromSeconds(10),
            Pooling = false,
            BackupRetention = 3
        };

        var interceptor = new SqliteConnectionInterceptor(Options);
        Factory = new OpenBarDbContextFactory(Options, interceptor);
    }

    public string DirectoryPath { get; }

    public OpenBarDatabaseOptions Options { get; }

    public OpenBarDbContextFactory Factory { get; }

    public async Task InitializeAsync()
    {
        using var initializer = new SqliteDatabaseInitializer(
            Options,
            Factory,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        _ = await initializer.InitializeAsync();
    }

    public WidgetInstanceService CreateInstanceService() => new(Factory);

    public async ValueTask DisposeAsync()
    {
        SqliteConnection.ClearAllPools();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
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
    }
}

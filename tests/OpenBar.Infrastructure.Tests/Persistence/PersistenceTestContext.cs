using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Infrastructure.Persistence;

namespace OpenBar.Infrastructure.Tests.Persistence;

/// <summary>
/// Isolated temporary SQLite database for real-provider tests. Pooling is
/// disabled so the folder can be removed deterministically on Windows.
/// </summary>
internal sealed class PersistenceTestContext : IAsyncDisposable
{
    private bool _disposed;

    public PersistenceTestContext()
    {
        DirectoryPath = Path.Combine(
            Path.GetTempPath(),
            "OpenBar.Tests",
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

        Interceptor = new SqliteConnectionInterceptor(Options);
        Factory = new OpenBarDbContextFactory(Options, Interceptor);
    }

    public string DirectoryPath { get; }

    public OpenBarDatabaseOptions Options { get; }

    public SqliteConnectionInterceptor Interceptor { get; }

    public OpenBarDbContextFactory Factory { get; }

    public SqliteDatabaseInitializer CreateInitializer()
        => new(Options, Factory, NullLogger<SqliteDatabaseInitializer>.Instance);

    public AppSettingsService CreateAppSettingsService() => new(Factory);

    public WidgetInstanceService CreateWidgetInstanceService() => new(Factory);

    public DockLayoutService CreateDockLayoutService() => new(Factory);

    public PinnedApplicationService CreatePinnedApplicationService() => new(Factory);

    public Task<OpenBarDbContext> CreateDbContextAsync()
        => Task.FromResult(Factory.CreateDbContext());

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
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

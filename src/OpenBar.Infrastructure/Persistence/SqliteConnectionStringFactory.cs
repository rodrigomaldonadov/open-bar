using Microsoft.Data.Sqlite;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>Builds the single SQLite connection string used by the whole application.</summary>
internal static class SqliteConnectionStringFactory
{
    public static string Create(OpenBarDatabaseOptions options, string? dataSource = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dataSource ?? options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            ForeignKeys = true,
            DefaultTimeout = Math.Max(1, (int)Math.Ceiling(options.BusyTimeout.TotalSeconds)),
            Pooling = options.Pooling
        };

        return builder.ToString();
    }
}

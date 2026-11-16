namespace OpenBar.Application.Persistence;

/// <summary>
/// Result of a persistence initialization pass. Paths are absolute and are
/// safe to log; they never contain user credentials.
/// </summary>
/// <param name="DatabasePath">Absolute path of the SQLite database file.</param>
/// <param name="DatabaseCreated">True when this pass created a new database file.</param>
/// <param name="AppliedMigrations">Migrations applied by this pass (empty when already current).</param>
/// <param name="BackupPath">Backup taken before migrations, when a backup was required.</param>
/// <param name="QuarantinedDatabasePath">Preserved corrupt database, when recovery was needed.</param>
public sealed record DatabaseInitializationResult(
    string DatabasePath,
    bool DatabaseCreated,
    IReadOnlyList<string> AppliedMigrations,
    string? BackupPath,
    string? QuarantinedDatabasePath);

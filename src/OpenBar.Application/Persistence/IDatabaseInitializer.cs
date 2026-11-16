namespace OpenBar.Application.Persistence;

/// <summary>
/// Creates the database directory/file, verifies integrity, backs up an
/// existing database before pending migrations and applies migrations.
/// Implementations must be safe to call concurrently and idempotently.
/// </summary>
public interface IDatabaseInitializer
{
    /// <summary>
    /// Initializes the database. Returns the already-completed result when a
    /// previous pass succeeded in this process.
    /// </summary>
    Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default);
}

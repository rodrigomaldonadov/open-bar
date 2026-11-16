using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Persistence;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// Creates the SQLite directory/file, checks integrity, applies EF Core
/// migrations and configures WAL. Runs on a background thread through
/// <see cref="PersistenceStartupService"/>.
///
/// Safety rules:
/// - Never delete a user database automatically. A corrupt file is moved to
///   <c>backups/corrupt</c> with its sidecar files and preserved.
/// - A pre-migration online backup is mandatory when migrations are pending.
/// - Cross-process initialization is serialized with an exclusive lock file.
/// - Only one initialization pass runs per process instance.
/// </summary>
public sealed partial class SqliteDatabaseInitializer : IDatabaseInitializer, IDisposable
{
    private readonly OpenBarDatabaseOptions _options;
    private readonly IDbContextFactory<OpenBarDbContext> _contextFactory;
    private readonly ILogger<SqliteDatabaseInitializer> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private DatabaseInitializationResult? _completed;
    private bool _disposed;

    public SqliteDatabaseInitializer(
        OpenBarDatabaseOptions options,
        IDbContextFactory<OpenBarDbContext> contextFactory,
        ILogger<SqliteDatabaseInitializer> logger)
    {
        _options = options;
        _contextFactory = contextFactory;
        _logger = logger;
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_completed is not null)
            {
                return _completed;
            }

            _options.Validate();
            Directory.CreateDirectory(_options.DirectoryPath);

            using var migrationLock = await AcquireMigrationLockAsync(cancellationToken).ConfigureAwait(false);
            _completed = await InitializeCoreAsync(cancellationToken).ConfigureAwait(false);

            LogDatabaseReady(
                _logger,
                _completed.DatabasePath,
                _completed.DatabaseCreated,
                _completed.AppliedMigrations.Count);

            return _completed;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<DatabaseInitializationResult> InitializeCoreAsync(CancellationToken cancellationToken)
    {
        string? quarantinedPath = null;

        // At most one recovery attempt: a corrupt file is preserved and a fresh
        // database is created. Any second corruption is treated as fatal.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var databaseExisted = File.Exists(_options.DatabasePath)
                && new FileInfo(_options.DatabasePath).Length > 0;

            try
            {
                if (databaseExisted)
                {
                    await VerifyIntegrityAsync(cancellationToken).ConfigureAwait(false);
                }

                var pending = await GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);

                string? backupPath = null;
                if (databaseExisted && pending.Count > 0)
                {
                    backupPath = await CreateBackupAsync(cancellationToken).ConfigureAwait(false);
                    PruneBackups();
                }

                await ApplyMigrationsAsync(pending, cancellationToken).ConfigureAwait(false);

                if (_options.EnableWal)
                {
                    await EnableWalAsync(cancellationToken).ConfigureAwait(false);
                }

                return new DatabaseInitializationResult(
                    _options.DatabasePath,
                    DatabaseCreated: !databaseExisted,
                    pending,
                    backupPath,
                    quarantinedPath);
            }
            catch (Exception ex) when (attempt == 0 && IsCorruption(ex))
            {
                LogCorruptionDetected(_logger, ex, _options.DatabasePath);
                quarantinedPath = await QuarantineDatabaseAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DatabaseInitializationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogInitializationFailed(_logger, ex, _options.DatabasePath);
                throw new DatabaseInitializationException(
                    $"OpenBar could not initialize the database at '{_options.DatabasePath}'. The file was not deleted.",
                    ex);
            }
        }

        throw new DatabaseInitializationException(
            $"OpenBar could not recover the database at '{_options.DatabasePath}'. The original file was preserved.");
    }

    private async Task<IReadOnlyList<string>> GetPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false);
        return pending.ToArray();
    }

    private async Task ApplyMigrationsAsync(
        IReadOnlyList<string> pending,
        CancellationToken cancellationToken)
    {
        if (pending.Count == 0)
        {
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);

        foreach (var migration in pending)
        {
            LogMigrationApplied(_logger, migration);
        }
    }

    private async Task VerifyIntegrityAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(SqliteConnectionStringFactory.Create(_options));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA quick_check;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;

        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new CorruptDatabaseException(
                $"SQLite quick_check reported '{result ?? "no result"}'.");
        }
    }

    private async Task EnableWalAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(SqliteConnectionStringFactory.Create(_options));
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL;";
        var mode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;

        if (!string.Equals(mode, "wal", StringComparison.OrdinalIgnoreCase))
        {
            LogWalNotEnabled(_logger, mode);
        }
    }

    private async Task<string> CreateBackupAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_options.BackupsDirectoryPath);
            cancellationToken.ThrowIfCancellationRequested();

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var backupPath = Path.Combine(
                _options.BackupsDirectoryPath,
                $"openbar-{timestamp}-{Guid.NewGuid():N}.db");

            await using var source = new SqliteConnection(SqliteConnectionStringFactory.Create(_options));
            await source.OpenAsync(cancellationToken).ConfigureAwait(false);

            var destinationBuilder = new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            await using var destination = new SqliteConnection(destinationBuilder.ToString());
            await destination.OpenAsync(cancellationToken).ConfigureAwait(false);

            source.BackupDatabase(destination);
            LogBackupCreated(_logger, backupPath);
            return backupPath;
        }
        catch (Exception ex) when (ex is not DatabaseInitializationException)
        {
            throw new DatabaseInitializationException(
                "OpenBar could not create a safety backup before applying migrations, so no migration was applied.",
                ex);
        }
    }

    private void PruneBackups()
    {
        try
        {
            var backups = Directory.EnumerateFiles(_options.BackupsDirectoryPath, "openbar-*.db")
                .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
                .Skip(_options.BackupRetention)
                .ToArray();

            foreach (var backup in backups)
            {
                File.Delete(backup);
                LogBackupPruned(_logger, backup);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Retention is best effort; never fail startup because an old
            // backup could not be removed.
            LogBackupPruneFailed(_logger, ex);
        }
    }

    private async Task<string> QuarantineDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(_options.CorruptDirectoryPath);

            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var basePath = Path.Combine(
                _options.CorruptDirectoryPath,
                $"{_options.DatabaseFileName}.{timestamp}.{Guid.NewGuid():N}.corrupt");

            File.Move(_options.DatabasePath, basePath);

            foreach (var suffix in new[] { "-wal", "-shm", "-journal" })
            {
                var sidecar = _options.DatabasePath + suffix;
                if (!File.Exists(sidecar))
                {
                    continue;
                }

                try
                {
                    File.Move(sidecar, basePath + suffix);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    LogSidecarQuarantineFailed(_logger, ex, suffix);
                }
            }

            LogDatabaseQuarantined(_logger, basePath);
            return basePath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DatabaseInitializationException(
                "The database appears corrupt, but OpenBar could not move it aside. Nothing was deleted.",
                ex);
        }
    }

    private async Task<FileStream> AcquireMigrationLockAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + _options.InitializationLockTimeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    _options.MigrationLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.DeleteOnClose);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new DatabaseInitializationException(
                    "Timed out waiting for another OpenBar process to finish database initialization.",
                    ex);
            }
        }
    }

    private static bool IsCorruption(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is CorruptDatabaseException)
            {
                return true;
            }

            if (current is SqliteException sqlite
                && (sqlite.SqliteErrorCode == 11
                    || sqlite.SqliteErrorCode == 26
                    || sqlite.Message.Contains("file is not a database", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "SQLite database ready at {DatabasePath} (created: {Created}, migrations applied: {MigrationCount}).")]
    private static partial void LogDatabaseReady(
        ILogger<SqliteDatabaseInitializer> logger,
        string databasePath,
        bool created,
        int migrationCount);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Information, Message = "Applied database migration {Migration}.")]
    private static partial void LogMigrationApplied(ILogger<SqliteDatabaseInitializer> logger, string migration);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "SQLite did not enable WAL mode; journal mode reported: {JournalMode}.")]
    private static partial void LogWalNotEnabled(ILogger<SqliteDatabaseInitializer> logger, string? journalMode);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information, Message = "Pre-migration database backup created at {BackupPath}.")]
    private static partial void LogBackupCreated(ILogger<SqliteDatabaseInitializer> logger, string backupPath);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Information, Message = "Pruned old database backup {BackupPath}.")]
    private static partial void LogBackupPruned(ILogger<SqliteDatabaseInitializer> logger, string backupPath);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Warning, Message = "Could not prune old database backups; retention is best effort.")]
    private static partial void LogBackupPruneFailed(ILogger<SqliteDatabaseInitializer> logger, Exception exception);

    [LoggerMessage(EventId = 4007, Level = LogLevel.Critical, Message = "SQLite reported corruption for {DatabasePath}; the original file will be preserved.")]
    private static partial void LogCorruptionDetected(
        ILogger<SqliteDatabaseInitializer> logger,
        Exception exception,
        string databasePath);

    [LoggerMessage(EventId = 4008, Level = LogLevel.Critical, Message = "The corrupt database was preserved at {QuarantinedPath}. A new database will be created; no user data was deleted.")]
    private static partial void LogDatabaseQuarantined(ILogger<SqliteDatabaseInitializer> logger, string quarantinedPath);

    [LoggerMessage(EventId = 4009, Level = LogLevel.Warning, Message = "Could not preserve the SQLite sidecar file {Suffix} while quarantining a corrupt database.")]
    private static partial void LogSidecarQuarantineFailed(
        ILogger<SqliteDatabaseInitializer> logger,
        Exception exception,
        string suffix);

    [LoggerMessage(EventId = 4010, Level = LogLevel.Error, Message = "Database initialization failed for {DatabasePath}.")]
    private static partial void LogInitializationFailed(
        ILogger<SqliteDatabaseInitializer> logger,
        Exception exception,
        string databasePath);

    private sealed class CorruptDatabaseException : Exception
    {
        public CorruptDatabaseException(string message)
            : base(message)
        {
        }
    }
}

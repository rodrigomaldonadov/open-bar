namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// Central persistence configuration. The default directory is
/// <c>%LOCALAPPDATA%\OpenBar</c> and never depends on the install or working
/// directory. Tests override <see cref="DirectoryPath"/> with a temporary
/// folder.
/// </summary>
public sealed class OpenBarDatabaseOptions
{
    public const string ProductFolderName = "OpenBar";
    public const string DefaultDatabaseFileName = "openbar.db";
    public const string BackupsFolderName = "backups";
    public const string CorruptFolderName = "corrupt";

    public string DirectoryPath { get; init; } = GetDefaultDirectory();

    public string DatabaseFileName { get; init; } = DefaultDatabaseFileName;

    public TimeSpan BusyTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan InitializationLockTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public bool EnableWal { get; init; } = true;

    /// <summary>Connection pooling. Enabled in production, disabled by tests that delete temp folders.</summary>
    public bool Pooling { get; init; } = true;

    /// <summary>Number of pre-migration backups kept on disk.</summary>
    public int BackupRetention { get; init; } = 5;

    public string DatabasePath => Path.Combine(DirectoryPath, DatabaseFileName);

    public string BackupsDirectoryPath => Path.Combine(DirectoryPath, BackupsFolderName);

    public string CorruptDirectoryPath => Path.Combine(BackupsDirectoryPath, CorruptFolderName);

    public string MigrationLockPath => Path.Combine(DirectoryPath, DatabaseFileName + ".migration.lock");

    public static string GetDefaultDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName);

    public static OpenBarDatabaseOptions CreateDefault() => new();

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath))
        {
            throw new ArgumentException("A database directory is required.", nameof(DirectoryPath));
        }

        if (string.IsNullOrWhiteSpace(DatabaseFileName)
            || DatabaseFileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("A valid database file name is required.", nameof(DatabaseFileName));
        }

        if (BusyTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(BusyTimeout), "The busy timeout must be positive.");
        }

        if (InitializationLockTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InitializationLockTimeout),
                "The initialization lock timeout must be positive.");
        }

        if (BackupRetention < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BackupRetention), "At least one backup must be kept.");
        }
    }
}

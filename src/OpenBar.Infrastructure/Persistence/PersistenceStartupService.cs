using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Persistence;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// Hosted service that initializes persistence off the UI thread and loads the
/// persisted settings snapshot before the application shows its window. Stop
/// clears SQLite connection pools so the database file is fully released when
/// OpenBar exits.
/// </summary>
public sealed partial class PersistenceStartupService : IHostedService
{
    private readonly IDatabaseInitializer _initializer;
    private readonly IAppSettingsService _settings;
    private readonly PersistedConfigurationCache _cache;
    private readonly ILogger<PersistenceStartupService> _logger;

    public PersistenceStartupService(
        IDatabaseInitializer initializer,
        IAppSettingsService settings,
        PersistedConfigurationCache cache,
        ILogger<PersistenceStartupService> logger)
    {
        _initializer = initializer;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
        => Task.Run(() => StartCoreAsync(cancellationToken), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        // No custom connections are held between operations; clearing the ADO.NET
        // pool releases pooled handles deterministically during shutdown.
        SqliteConnection.ClearAllPools();
        LogPersistenceStopped(_logger);
        return Task.CompletedTask;
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        var result = await _initializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var settings = await _settings.GetAllAsync(cancellationToken).ConfigureAwait(false);
        _cache.Replace(settings);

        LogPersistenceStarted(
            _logger,
            result.DatabasePath,
            result.DatabaseCreated,
            result.AppliedMigrations.Count,
            settings.Count);
    }

    [LoggerMessage(EventId = 4101, Level = LogLevel.Information, Message = "Persistence initialized: database {DatabasePath} (created: {Created}, migrations applied: {MigrationCount}, settings loaded: {SettingCount}).")]
    private static partial void LogPersistenceStarted(
        ILogger<PersistenceStartupService> logger,
        string databasePath,
        bool created,
        int migrationCount,
        int settingCount);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Information, Message = "Persistence stopped; SQLite connection pools cleared.")]
    private static partial void LogPersistenceStopped(ILogger<PersistenceStartupService> logger);
}

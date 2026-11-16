using OpenBar.Domain.Settings;

namespace OpenBar.Application.Persistence;

/// <summary>
/// Persists global, non-secret key/value settings. Implementations must never
/// log setting values and must reject obvious credential keys.
/// </summary>
public interface IAppSettingsService
{
    Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Creates a new setting; throws <see cref="PersistenceConflictException"/> if it exists.</summary>
    Task<AppSetting> CreateAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing setting; throws <see cref="PersistenceNotFoundException"/> if missing.</summary>
    Task<AppSetting> UpdateAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates a setting in a single operation.</summary>
    Task<AppSetting> SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates several settings inside one transaction.</summary>
    Task SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

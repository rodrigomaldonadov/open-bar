using Microsoft.EntityFrameworkCore;
using OpenBar.Application.Persistence;
using OpenBar.Domain.Settings;

namespace OpenBar.Infrastructure.Persistence;

/// <inheritdoc cref="IAppSettingsService" />
public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IDbContextFactory<OpenBarDbContext> _contextFactory;

    public AppSettingsService(IDbContextFactory<OpenBarDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.AppSettings
            .AsNoTracking()
            .OrderBy(setting => setting.Key)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IsValidReadKey(key))
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.AppSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        => (await GetAsync(key, cancellationToken).ConfigureAwait(false))?.Value;

    public async Task<AppSetting> CreateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        Validate(key, value);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var exists = await context.AppSettings
            .AsNoTracking()
            .AnyAsync(setting => setting.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (exists)
        {
            throw new PersistenceConflictException("A setting with the same key already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new AppSetting
        {
            Key = key,
            Value = value,
            CreatedAt = now,
            UpdatedAt = now
        };

        context.AppSettings.Add(entity);
        await SaveAsync(context, "Creating the setting", cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task<AppSetting> UpdateAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        Validate(key, value);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var entity = await context.AppSettings
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new PersistenceNotFoundException("The setting does not exist.");
        }

        entity.Value = value;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(context, "Updating the setting", cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task<AppSetting> SetAsync(
        string key,
        string value,
        CancellationToken cancellationToken = default)
    {
        Validate(key, value);

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var entity = await context.AppSettings
            .FirstOrDefaultAsync(setting => setting.Key == key, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new AppSetting
            {
                Key = key,
                Value = value,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.AppSettings.Add(entity);
        }
        else
        {
            entity.Value = value;
            entity.UpdatedAt = now;
        }

        await SaveAsync(context, "Saving the setting", cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);

        foreach (var pair in values)
        {
            Validate(pair.Key, pair.Value);
        }

        if (values.Count == 0)
        {
            return;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var keys = values.Keys.ToArray();
        var existing = await context.AppSettings
            .Where(setting => keys.Contains(setting.Key))
            .ToDictionaryAsync(setting => setting.Key, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        foreach (var pair in values)
        {
            if (existing.TryGetValue(pair.Key, out var entity))
            {
                entity.Value = pair.Value;
                entity.UpdatedAt = now;
            }
            else
            {
                context.AppSettings.Add(new AppSetting
                {
                    Key = pair.Key,
                    Value = pair.Value,
                    CreatedAt = now,
                    UpdatedAt = now
                });
            }
        }

        await SaveAsync(context, "Saving the settings", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        if (!IsValidReadKey(key))
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await context.AppSettings
            .Where(setting => setting.Key == key)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    private static async Task SaveAsync(
        OpenBarDbContext context,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            throw PersistenceErrors.Translate(ex, operation);
        }
    }

    private static bool IsValidReadKey(string key)
        => !string.IsNullOrWhiteSpace(key) && key.Length <= AppSetting.MaxKeyLength;

    private static void Validate(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PersistenceValidationException("A setting key is required.");
        }

        if (key.Length > AppSetting.MaxKeyLength)
        {
            throw new PersistenceValidationException("The setting key is too long.");
        }

        if (value.Length > AppSetting.MaxValueLength)
        {
            throw new PersistenceValidationException("The setting value is too long.");
        }

        if (SettingKeyPolicy.IsSensitive(key))
        {
            throw new PersistenceValidationException(
                "Secret material must not be stored in the settings table; use the Windows secret store.");
        }
    }
}

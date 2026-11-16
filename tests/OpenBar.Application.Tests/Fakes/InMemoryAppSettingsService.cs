using OpenBar.Application.Persistence;
using OpenBar.Domain.Settings;

namespace OpenBar.Application.Tests.Fakes;

/// <summary>
/// Deterministic in-memory settings store used to unit test Application-layer
/// consumers without a database. It mirrors the ordering and timestamp
/// semantics of the real service.
/// </summary>
internal sealed class InMemoryAppSettingsService : IAppSettingsService
{
    private readonly Dictionary<string, AppSetting> _items = new(StringComparer.Ordinal);

    public int GetAllCallCount { get; private set; }

    public int SetManyCallCount { get; private set; }

    public Task<IReadOnlyList<AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        GetAllCallCount++;

        IReadOnlyList<AppSetting> snapshot = _items.Values
            .OrderBy(setting => setting.Key, StringComparer.Ordinal)
            .Select(Clone)
            .ToArray();

        return Task.FromResult(snapshot);
    }

    public Task<AppSetting?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.TryGetValue(key, out var setting) ? Clone(setting) : null);

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
        => (await GetAsync(key, cancellationToken).ConfigureAwait(false))?.Value;

    public Task<AppSetting> CreateAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (_items.ContainsKey(key))
        {
            throw new PersistenceConflictException("A setting with the same key already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var setting = new AppSetting
        {
            Key = key,
            Value = value,
            CreatedAt = now,
            UpdatedAt = now
        };

        _items[key] = setting;
        return Task.FromResult(Clone(setting));
    }

    public async Task<AppSetting> UpdateAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        if (!_items.TryGetValue(key, out var setting))
        {
            throw new PersistenceNotFoundException("The setting does not exist.");
        }

        setting.Value = value;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        return await Task.FromResult(Clone(setting)).ConfigureAwait(false);
    }

    public async Task<AppSetting> SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        if (_items.TryGetValue(key, out var setting))
        {
            setting.Value = value;
            setting.UpdatedAt = now;
        }
        else
        {
            setting = new AppSetting
            {
                Key = key,
                Value = value,
                CreatedAt = now,
                UpdatedAt = now
            };
            _items[key] = setting;
        }

        return await Task.FromResult(Clone(setting)).ConfigureAwait(false);
    }

    public Task SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        SetManyCallCount++;
        var now = DateTimeOffset.UtcNow;

        foreach (var pair in values)
        {
            if (_items.TryGetValue(pair.Key, out var setting))
            {
                setting.Value = pair.Value;
                setting.UpdatedAt = now;
            }
            else
            {
                _items[pair.Key] = new AppSetting
                {
                    Key = pair.Key,
                    Value = pair.Value,
                    CreatedAt = now,
                    UpdatedAt = now
                };
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.Remove(key));

    private static AppSetting Clone(AppSetting setting)
        => new()
        {
            Key = setting.Key,
            Value = setting.Value,
            CreatedAt = setting.CreatedAt,
            UpdatedAt = setting.UpdatedAt
        };
}

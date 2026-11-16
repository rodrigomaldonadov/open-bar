using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

/// <summary>
/// Minimal instance store used for lifecycle tests that do not exercise the
/// real provider. Persistence behaviour is covered separately with SQLite.
/// </summary>
internal sealed class InMemoryWidgetInstanceService : OpenBar.Application.Persistence.IWidgetInstanceService
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, OpenBar.Domain.Widgets.WidgetInstance> _items = new();

    public Task<IReadOnlyList<OpenBar.Domain.Widgets.WidgetInstance>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OpenBar.Domain.Widgets.WidgetInstance>>(ReadAll(null));

    public Task<IReadOnlyList<OpenBar.Domain.Widgets.WidgetInstance>> GetByWidgetIdAsync(
        string widgetId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OpenBar.Domain.Widgets.WidgetInstance>>(ReadAll(widgetId));

    public Task<IReadOnlyList<OpenBar.Domain.Widgets.WidgetInstance>> GetEnabledAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OpenBar.Domain.Widgets.WidgetInstance>>(
            ReadAll(null).Where(instance => instance.IsEnabled).ToArray());

    public Task<OpenBar.Domain.Widgets.WidgetInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(
                _items.TryGetValue(id, out var instance) ? Clone(instance) : null);
        }
    }

    public Task<OpenBar.Domain.Widgets.WidgetInstance> CreateAsync(
        OpenBar.Domain.Widgets.WidgetInstance instance,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (instance.Id == Guid.Empty)
            {
                instance.Id = Guid.NewGuid();
            }

            if (_items.Values.Any(existing =>
                    existing.WidgetId == instance.WidgetId
                    && existing.InstanceName == instance.InstanceName))
            {
                throw new OpenBar.Application.Persistence.PersistenceConflictException(
                    "A widget instance with the same name already exists.");
            }

            var now = DateTimeOffset.UtcNow;
            instance.CreatedAt = instance.CreatedAt == default ? now : instance.CreatedAt;
            instance.UpdatedAt = now;
            _items[instance.Id] = Clone(instance);
            return Task.FromResult(Clone(instance));
        }
    }

    public Task<OpenBar.Domain.Widgets.WidgetInstance> UpdateAsync(
        OpenBar.Domain.Widgets.WidgetInstance instance,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_items.ContainsKey(instance.Id))
            {
                throw new OpenBar.Application.Persistence.PersistenceNotFoundException("The instance does not exist.");
            }

            instance.UpdatedAt = DateTimeOffset.UtcNow;
            _items[instance.Id] = Clone(instance);
            return Task.FromResult(Clone(instance));
        }
    }

    public Task<OpenBar.Domain.Widgets.WidgetInstance> UpdateSettingsAsync(
        Guid id,
        string settingsJson,
        int settingsSchemaVersion,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_items.TryGetValue(id, out var instance))
            {
                throw new OpenBar.Application.Persistence.PersistenceNotFoundException("The instance does not exist.");
            }

            instance.SettingsJson = settingsJson;
            instance.SettingsSchemaVersion = settingsSchemaVersion;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(Clone(instance));
        }
    }

    public Task<OpenBar.Domain.Widgets.WidgetInstance> UpdatePresentationAsync(
        Guid id,
        OpenBar.Domain.Widgets.WidgetPresentationMode presentationMode,
        int presentationOrder,
        CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_items.TryGetValue(id, out var instance))
            {
                throw new OpenBar.Application.Persistence.PersistenceNotFoundException("The instance does not exist.");
            }

            instance.PresentationMode = presentationMode;
            instance.PresentationOrder = presentationOrder;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(Clone(instance));
        }
    }

    public Task<bool> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_items.TryGetValue(id, out var instance))
            {
                return Task.FromResult(false);
            }

            instance.IsEnabled = isEnabled;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(true);
        }
    }

    public Task SaveManyAsync(
        IReadOnlyList<OpenBar.Domain.Widgets.WidgetInstance> instances,
        CancellationToken cancellationToken = default)
    {
        foreach (var instance in instances)
        {
            if (instance.Id == Guid.Empty)
            {
                _ = CreateAsync(instance, cancellationToken);
            }
            else
            {
                _ = UpdateAsync(instance, cancellationToken);
            }
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            return Task.FromResult(_items.Remove(id));
        }
    }

    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _items.Count;
            }
        }
    }

    public OpenBar.Domain.Widgets.WidgetInstance? Find(Guid id)
    {
        lock (_sync)
        {
            return _items.TryGetValue(id, out var instance) ? Clone(instance) : null;
        }
    }

    private OpenBar.Domain.Widgets.WidgetInstance[] ReadAll(string? widgetId)
    {
        lock (_sync)
        {
            return _items.Values
                .Where(instance => widgetId is null || instance.WidgetId == widgetId)
                .OrderBy(instance => instance.WidgetId, StringComparer.Ordinal)
                .ThenBy(instance => instance.InstanceName, StringComparer.Ordinal)
                .Select(Clone)
                .ToArray();
        }
    }

    private static OpenBar.Domain.Widgets.WidgetInstance Clone(OpenBar.Domain.Widgets.WidgetInstance instance)
        => new()
        {
            Id = instance.Id,
            WidgetId = instance.WidgetId,
            InstanceName = instance.InstanceName,
            IsEnabled = instance.IsEnabled,
            PresentationMode = instance.PresentationMode,
            PresentationOrder = instance.PresentationOrder,
            PositionX = instance.PositionX,
            PositionY = instance.PositionY,
            Width = instance.Width,
            Height = instance.Height,
            SettingsJson = instance.SettingsJson,
            SettingsSchemaVersion = instance.SettingsSchemaVersion,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt
        };
}

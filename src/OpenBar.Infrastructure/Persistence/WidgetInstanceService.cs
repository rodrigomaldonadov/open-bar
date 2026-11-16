using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenBar.Application.Persistence;
using OpenBar.Domain.Widgets;

namespace OpenBar.Infrastructure.Persistence;

/// <inheritdoc cref="IWidgetInstanceService" />
public sealed class WidgetInstanceService : IWidgetInstanceService
{
    private readonly IDbContextFactory<OpenBarDbContext> _contextFactory;

    public WidgetInstanceService(IDbContextFactory<OpenBarDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<WidgetInstance>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.WidgetInstances
            .AsNoTracking()
            .OrderBy(instance => instance.WidgetId)
            .ThenBy(instance => instance.InstanceName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WidgetInstance>> GetByWidgetIdAsync(
        string widgetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(widgetId))
        {
            return [];
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.WidgetInstances
            .AsNoTracking()
            .Where(instance => instance.WidgetId == widgetId)
            .OrderBy(instance => instance.InstanceName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WidgetInstance>> GetEnabledAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.WidgetInstances
            .AsNoTracking()
            .Where(instance => instance.IsEnabled)
            .OrderBy(instance => instance.WidgetId)
            .ThenBy(instance => instance.InstanceName)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<WidgetInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.WidgetInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(instance => instance.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<WidgetInstance> CreateAsync(
        WidgetInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Validate(instance);

        if (instance.Id == Guid.Empty)
        {
            instance.Id = Guid.NewGuid();
        }

        var now = DateTimeOffset.UtcNow;
        if (instance.CreatedAt == default)
        {
            instance.CreatedAt = now;
        }

        instance.UpdatedAt = now;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.WidgetInstances.Add(instance);
        await SaveAsync(context, "Creating the widget instance", cancellationToken).ConfigureAwait(false);
        return instance;
    }

    public async Task<WidgetInstance> UpdateAsync(
        WidgetInstance instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        Validate(instance);

        if (instance.Id == Guid.Empty)
        {
            throw new PersistenceValidationException("A widget instance id is required for an update.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await context.WidgetInstances
            .FirstOrDefaultAsync(existing => existing.Id == instance.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new PersistenceNotFoundException("The widget instance does not exist.");
        }

        Apply(entity, instance, DateTimeOffset.UtcNow);
        await SaveAsync(context, "Updating the widget instance", cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task<WidgetInstance> UpdateSettingsAsync(
        Guid id,
        string settingsJson,
        int settingsSchemaVersion,
        CancellationToken cancellationToken = default)
    {
        ValidateSettings(settingsJson, settingsSchemaVersion);

        if (id == Guid.Empty)
        {
            throw new PersistenceValidationException("A widget instance id is required.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await context.WidgetInstances
            .FirstOrDefaultAsync(instance => instance.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new PersistenceNotFoundException("The widget instance does not exist.");
        }

        entity.SettingsJson = settingsJson;
        entity.SettingsSchemaVersion = settingsSchemaVersion;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(context, "Updating the widget settings", cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task<WidgetInstance> UpdatePresentationAsync(
        Guid id,
        WidgetPresentationMode presentationMode,
        int presentationOrder,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new PersistenceValidationException("A widget instance id is required.");
        }

        if (presentationOrder < 0)
        {
            throw new PersistenceValidationException("The widget presentation order cannot be negative.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var entity = await context.WidgetInstances
            .FirstOrDefaultAsync(instance => instance.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new PersistenceNotFoundException("The widget instance does not exist.");
        }

        entity.PresentationMode = presentationMode;
        entity.PresentationOrder = presentationOrder;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(context, "Updating the widget presentation", cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task<bool> SetEnabledAsync(
        Guid id,
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var updated = await context.WidgetInstances
            .Where(instance => instance.Id == id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(instance => instance.IsEnabled, isEnabled)
                    .SetProperty(instance => instance.UpdatedAt, DateTimeOffset.UtcNow),
                cancellationToken)
            .ConfigureAwait(false);

        return updated > 0;
    }

    public async Task SaveManyAsync(
        IReadOnlyList<WidgetInstance> instances,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instances);

        foreach (var instance in instances)
        {
            ArgumentNullException.ThrowIfNull(instance);
            Validate(instance);
        }

        if (instances.Count == 0)
        {
            return;
        }

        var ids = instances
            .Where(instance => instance.Id != Guid.Empty)
            .Select(instance => instance.Id)
            .ToArray();

        if (ids.Length != ids.Distinct().Count())
        {
            throw new PersistenceValidationException("The widget batch contains duplicate ids.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var existing = ids.Length == 0
            ? new Dictionary<Guid, WidgetInstance>()
            : await context.WidgetInstances
                .Where(instance => ids.Contains(instance.Id))
                .ToDictionaryAsync(instance => instance.Id, cancellationToken)
                .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        foreach (var source in instances)
        {
            if (source.Id == Guid.Empty)
            {
                context.WidgetInstances.Add(CreateNew(source, now));
                continue;
            }

            if (existing.TryGetValue(source.Id, out var entity))
            {
                Apply(entity, source, now);
                continue;
            }

            var inserted = CreateNew(source, now);
            inserted.Id = source.Id;
            inserted.CreatedAt = source.CreatedAt == default ? now : source.CreatedAt;
            context.WidgetInstances.Add(inserted);
        }

        await SaveAsync(context, "Saving the widget instances", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await context.WidgetInstances
            .Where(instance => instance.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    private static WidgetInstance CreateNew(WidgetInstance source, DateTimeOffset now)
        => new()
        {
            Id = source.Id == Guid.Empty ? Guid.NewGuid() : source.Id,
            WidgetId = source.WidgetId,
            InstanceName = source.InstanceName,
            IsEnabled = source.IsEnabled,
            PresentationMode = source.PresentationMode,
            PresentationOrder = source.PresentationOrder,
            PositionX = source.PositionX,
            PositionY = source.PositionY,
            Width = source.Width,
            Height = source.Height,
            SettingsJson = source.SettingsJson,
            SettingsSchemaVersion = source.SettingsSchemaVersion,
            CreatedAt = source.CreatedAt == default ? now : source.CreatedAt,
            UpdatedAt = now
        };

    private static void Apply(WidgetInstance target, WidgetInstance source, DateTimeOffset now)
    {
        target.WidgetId = source.WidgetId;
        target.InstanceName = source.InstanceName;
        target.IsEnabled = source.IsEnabled;
        target.PresentationMode = source.PresentationMode;
        target.PresentationOrder = source.PresentationOrder;
        target.PositionX = source.PositionX;
        target.PositionY = source.PositionY;
        target.Width = source.Width;
        target.Height = source.Height;
        target.SettingsJson = source.SettingsJson;
        target.SettingsSchemaVersion = source.SettingsSchemaVersion;
        target.UpdatedAt = now;
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

    private static void Validate(WidgetInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.WidgetId)
            || instance.WidgetId.Length > WidgetInstance.MaxWidgetIdLength)
        {
            throw new PersistenceValidationException("A valid widget type id is required.");
        }

        if (string.IsNullOrWhiteSpace(instance.InstanceName)
            || instance.InstanceName.Length > WidgetInstance.MaxInstanceNameLength)
        {
            throw new PersistenceValidationException("A valid widget instance name is required.");
        }

        ValidateSettings(instance.SettingsJson, instance.SettingsSchemaVersion);

        if (instance.Width is <= 0)
        {
            throw new PersistenceValidationException("The widget width must be positive when specified.");
        }

        if (instance.Height is <= 0)
        {
            throw new PersistenceValidationException("The widget height must be positive when specified.");
        }
    }

    private static void ValidateSettings(string settingsJson, int settingsSchemaVersion)
    {
        if (settingsJson is null)
        {
            throw new PersistenceValidationException("Widget settings JSON is required.");
        }

        if (settingsSchemaVersion < 1)
        {
            throw new PersistenceValidationException("The widget settings schema version must be at least 1.");
        }

        try
        {
            using var document = JsonDocument.Parse(settingsJson);
        }
        catch (JsonException ex)
        {
            throw new PersistenceValidationException("Widget settings must be valid JSON.", ex);
        }
    }
}

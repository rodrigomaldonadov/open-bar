using Microsoft.EntityFrameworkCore;
using OpenBar.Application.Persistence;
using OpenBar.Domain.Layouts;

namespace OpenBar.Infrastructure.Persistence;

/// <inheritdoc cref="IDockLayoutService" />
public sealed class DockLayoutService : IDockLayoutService
{
    private readonly IDbContextFactory<OpenBarDbContext> _contextFactory;

    public DockLayoutService(IDbContextFactory<OpenBarDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<DockLayout>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.DockLayouts
            .AsNoTracking()
            .OrderBy(layout => layout.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DockLayout?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.DockLayouts
            .AsNoTracking()
            .FirstOrDefaultAsync(layout => layout.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DockLayout?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.DockLayouts
            .AsNoTracking()
            .FirstOrDefaultAsync(layout => layout.IsDefault, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<DockLayout> CreateAsync(
        DockLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Validate(layout);

        if (layout.Id == Guid.Empty)
        {
            layout.Id = Guid.NewGuid();
        }

        var now = DateTimeOffset.UtcNow;
        if (layout.CreatedAt == default)
        {
            layout.CreatedAt = now;
        }

        layout.UpdatedAt = now;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        if (layout.IsDefault)
        {
            await ClearOtherDefaultsAsync(context, layout.Id, cancellationToken).ConfigureAwait(false);
        }

        context.DockLayouts.Add(layout);
        await SaveAsync(context, "Creating the dock layout", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return layout;
    }

    public async Task<DockLayout> UpdateAsync(
        DockLayout layout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Validate(layout);

        if (layout.Id == Guid.Empty)
        {
            throw new PersistenceValidationException("A dock layout id is required for an update.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var entity = await context.DockLayouts
            .FirstOrDefaultAsync(existing => existing.Id == layout.Id, cancellationToken)
            .ConfigureAwait(false);

        if (entity is null)
        {
            throw new PersistenceNotFoundException("The dock layout does not exist.");
        }

        if (layout.IsDefault)
        {
            await ClearOtherDefaultsAsync(context, layout.Id, cancellationToken).ConfigureAwait(false);
        }

        entity.Name = layout.Name;
        entity.Position = layout.Position;
        entity.Alignment = layout.Alignment;
        entity.Size = layout.Size;
        entity.Offset = layout.Offset;
        entity.IconSize = layout.IconSize;
        entity.Spacing = layout.Spacing;
        entity.Magnification = layout.Magnification;
        entity.MaterialOpacity = layout.MaterialOpacity;
        entity.AnimationDurationMs = layout.AnimationDurationMs;
        entity.AutoHide = layout.AutoHide;
        entity.IsDefault = layout.IsDefault;
        entity.MonitorTarget = layout.MonitorTarget;
        entity.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(context, "Updating the dock layout", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            throw new PersistenceNotFoundException("The dock layout does not exist.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var target = await context.DockLayouts
            .FirstOrDefaultAsync(layout => layout.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (target is null)
        {
            throw new PersistenceNotFoundException("The dock layout does not exist.");
        }

        await ClearOtherDefaultsAsync(context, id, cancellationToken).ConfigureAwait(false);

        target.IsDefault = true;
        target.UpdatedAt = DateTimeOffset.UtcNow;

        await SaveAsync(context, "Setting the default dock layout", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await context.DockLayouts
            .Where(layout => layout.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    private static Task<int> ClearOtherDefaultsAsync(
        OpenBarDbContext context,
        Guid keepId,
        CancellationToken cancellationToken)
        => context.DockLayouts
            .Where(layout => layout.IsDefault && layout.Id != keepId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(layout => layout.IsDefault, false),
                cancellationToken);

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

    private static void ValidateRange(double value, double minimum, double maximum, string message)
    {
        if (!double.IsFinite(value) || value < minimum || value > maximum)
        {
            throw new PersistenceValidationException(message);
        }
    }

    private static void Validate(DockLayout layout)
    {
        if (string.IsNullOrWhiteSpace(layout.Name) || layout.Name.Length > DockLayout.MaxNameLength)
        {
            throw new PersistenceValidationException("A valid dock layout name is required.");
        }

        if (!Enum.IsDefined(layout.Position))
        {
            throw new PersistenceValidationException("The dock position is not valid.");
        }

        if (!Enum.IsDefined(layout.Alignment))
        {
            throw new PersistenceValidationException("The dock alignment is not valid.");
        }

        if (double.IsNaN(layout.Size) || double.IsInfinity(layout.Size) || layout.Size <= 0)
        {
            throw new PersistenceValidationException("The dock size must be a positive number.");
        }

        if (double.IsNaN(layout.Offset) || double.IsInfinity(layout.Offset) || layout.Offset < 0)
        {
            throw new PersistenceValidationException("The dock offset must be zero or positive.");
        }

        ValidateRange(layout.IconSize, 24, 96, "The dock icon size must be between 24 and 96 DIPs.");
        ValidateRange(layout.Spacing, 0, 40, "The dock spacing must be between 0 and 40 DIPs.");
        ValidateRange(layout.Magnification, 1, 1.5, "The dock magnification must be between 1.0 and 1.5.");
        ValidateRange(layout.MaterialOpacity, 0.05, 1, "The dock material opacity must be between 0.05 and 1.0.");

        if (layout.AnimationDurationMs is < 0 or > 1_000)
        {
            throw new PersistenceValidationException("The dock animation duration must be between 0 and 1000 ms.");
        }

        if (string.IsNullOrWhiteSpace(layout.MonitorTarget)
            || layout.MonitorTarget.Length > DockLayout.MaxMonitorTargetLength)
        {
            throw new PersistenceValidationException("A valid logical monitor target is required.");
        }

        layout.MonitorTarget = layout.MonitorTarget.Trim();
    }
}

using Microsoft.EntityFrameworkCore;
using OpenBar.Application.Persistence;
using OpenBar.Domain.Dock;

namespace OpenBar.Infrastructure.Persistence;

/// <inheritdoc cref="IPinnedApplicationService" />
public sealed class PinnedApplicationService : IPinnedApplicationService
{
    private readonly IDbContextFactory<OpenBarDbContext> _contextFactory;

    public PinnedApplicationService(IDbContextFactory<OpenBarDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<IReadOnlyList<PinnedApplication>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.PinnedApplications
            .AsNoTracking()
            .OrderBy(application => application.SortOrder)
            .ThenBy(application => application.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PinnedApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return null;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return await context.PinnedApplications
            .AsNoTracking()
            .FirstOrDefaultAsync(application => application.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<PinnedApplication> AddAsync(
        PinnedApplication application,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(application);
        Validate(application);

        if (application.Id == Guid.Empty)
        {
            application.Id = Guid.NewGuid();
        }

        var now = DateTimeOffset.UtcNow;
        if (application.CreatedAt == default)
        {
            application.CreatedAt = now;
        }

        application.UpdatedAt = now;

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        context.PinnedApplications.Add(application);
        await SaveAsync(context, "Adding the pinned application", cancellationToken).ConfigureAwait(false);
        return application;
    }

    public async Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return false;
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var deleted = await context.PinnedApplications
            .Where(application => application.Id == id)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);

        return deleted > 0;
    }

    public async Task ReorderAsync(
        IReadOnlyList<Guid> orderedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedIds);

        if (orderedIds.Count != orderedIds.Distinct().Count())
        {
            throw new PersistenceValidationException("The pinned application order contains duplicate ids.");
        }

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await context.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var applications = await context.PinnedApplications
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (applications.Count != orderedIds.Count)
        {
            throw new PersistenceValidationException(
                "The pinned application order must include every existing item exactly once.");
        }

        var byId = applications.ToDictionary(application => application.Id);
        var now = DateTimeOffset.UtcNow;

        for (var index = 0; index < orderedIds.Count; index++)
        {
            if (!byId.TryGetValue(orderedIds[index], out var application))
            {
                throw new PersistenceValidationException("The pinned application order references an unknown item.");
            }

            application.SortOrder = index;
            application.UpdatedAt = now;
        }

        await SaveAsync(context, "Reordering pinned applications", cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
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

    private static void Validate(PinnedApplication application)
    {
        if (string.IsNullOrWhiteSpace(application.Name)
            || application.Name.Length > PinnedApplication.MaxNameLength)
        {
            throw new PersistenceValidationException("A valid pinned application name is required.");
        }

        if (string.IsNullOrWhiteSpace(application.Target)
            || application.Target.Length > PinnedApplication.MaxTargetLength)
        {
            throw new PersistenceValidationException("A valid pinned application target is required.");
        }

        if (!Enum.IsDefined(application.TargetKind))
        {
            throw new PersistenceValidationException("The pinned target kind is not valid.");
        }

        if (application.Arguments is { Length: > PinnedApplication.MaxArgumentsLength })
        {
            throw new PersistenceValidationException("The pinned application arguments are too long.");
        }

        if (application.ResolvedTarget is { Length: > PinnedApplication.MaxResolvedTargetLength })
        {
            throw new PersistenceValidationException("The resolved application target is too long.");
        }

        if (application.AppUserModelId is { Length: > PinnedApplication.MaxAppUserModelIdLength })
        {
            throw new PersistenceValidationException("The application user model id is too long.");
        }

        if (application.IconKey is { Length: > PinnedApplication.MaxIconKeyLength })
        {
            throw new PersistenceValidationException("The application icon key is too long.");
        }

        if (application.SortOrder < 0)
        {
            throw new PersistenceValidationException("The pinned application order must be zero or positive.");
        }
    }
}

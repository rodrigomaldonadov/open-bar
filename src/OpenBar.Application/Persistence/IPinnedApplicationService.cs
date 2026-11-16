using OpenBar.Domain.Dock;

namespace OpenBar.Application.Persistence;

/// <summary>Stores dock-pinned applications and their user-defined order.</summary>
public interface IPinnedApplicationService
{
    Task<IReadOnlyList<PinnedApplication>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<PinnedApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<PinnedApplication> AddAsync(PinnedApplication application, CancellationToken cancellationToken = default);

    Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Persists the full order inside one transaction.</summary>
    Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);
}

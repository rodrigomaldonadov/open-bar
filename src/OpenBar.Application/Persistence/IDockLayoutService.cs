using OpenBar.Domain.Layouts;

namespace OpenBar.Application.Persistence;

/// <summary>
/// Stores dock layouts and guarantees that at most one layout is the default.
/// </summary>
public interface IDockLayoutService
{
    Task<IReadOnlyList<DockLayout>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<DockLayout?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<DockLayout?> GetDefaultAsync(CancellationToken cancellationToken = default);

    Task<DockLayout> CreateAsync(DockLayout layout, CancellationToken cancellationToken = default);

    Task<DockLayout> UpdateAsync(DockLayout layout, CancellationToken cancellationToken = default);

    /// <summary>Marks one layout as default inside a transaction, clearing the previous default.</summary>
    Task SetDefaultAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

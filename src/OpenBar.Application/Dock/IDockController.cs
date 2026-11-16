using OpenBar.Domain.Layouts;

namespace OpenBar.Application.Dock;

/// <summary>
/// Use cases of the dock: pinned applications, merged running state, launch
/// and layout updates. Presentation consumes this interface; the implementation
/// lives in OpenBar.Dock and never references WinUI.
/// </summary>
public interface IDockController
{
    event EventHandler? Changed;

    DockLayout Layout { get; }

    IReadOnlyList<DockItemInfo> Items { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task RefreshRunningAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscoveredApplication>> DiscoverApplicationsAsync(
        CancellationToken cancellationToken = default);

    Task AddPinnedAsync(DiscoveredApplication application, CancellationToken cancellationToken = default);

    Task RemovePinnedAsync(Guid pinnedId, CancellationToken cancellationToken = default);

    Task ReorderPinnedAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default);

    Task ActivateAsync(DockItemInfo item, CancellationToken cancellationToken = default);

    Task UpdateLayoutAsync(DockLayout layout, CancellationToken cancellationToken = default);
}

namespace OpenBar.Application.Dock;

/// <summary>Discovers real applications installed in the Windows Start Menu.</summary>
public interface IApplicationCatalogService
{
    Task<IReadOnlyList<DiscoveredApplication>> DiscoverAsync(CancellationToken cancellationToken = default);
}

namespace OpenBar.Application.Dock;

/// <summary>
/// Enumerates visible top-level application windows. Implementations must
/// exclude the OpenBar process, shell surfaces and non-visible background
/// processes.
/// </summary>
public interface IAppWindowService
{
    Task<IReadOnlyList<AppWindowSnapshot>> GetVisibleWindowsAsync(CancellationToken cancellationToken = default);
}

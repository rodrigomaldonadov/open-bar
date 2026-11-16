namespace OpenBar.Application.Dock;

/// <summary>Launches pinned targets and activates existing windows.</summary>
public interface IApplicationLauncher
{
    Task LaunchAsync(DockItemInfo item, CancellationToken cancellationToken = default);

    Task ActivateWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default);
}

using OpenBar.Domain.Dock;

namespace OpenBar.Application.Dock;

/// <summary>Resolves .lnk targets and AppUserModelIds without launching the app.</summary>
public interface IShortcutResolver
{
    Task<ResolvedApplicationTarget> ResolveAsync(
        string target,
        PinnedTargetKind targetKind,
        CancellationToken cancellationToken = default);
}

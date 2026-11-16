using OpenBar.Domain.Dock;

namespace OpenBar.Application.Dock;

/// <summary>Application discovered from the Start Menu (real shortcut/exe, no hardcoded catalog).</summary>
public sealed record DiscoveredApplication(
    string Name,
    string Target,
    PinnedTargetKind TargetKind,
    string? Arguments = null,
    string? IconPath = null);

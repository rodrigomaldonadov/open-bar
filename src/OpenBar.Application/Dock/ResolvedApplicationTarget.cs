using OpenBar.Domain.Dock;

namespace OpenBar.Application.Dock;

/// <summary>Resolved metadata for a launch target (shortcut target and AUMID).</summary>
public sealed record ResolvedApplicationTarget(
    string? ExecutablePath,
    string? Arguments,
    string? AppUserModelId);

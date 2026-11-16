namespace OpenBar.Application.Dock;

/// <summary>One visible top-level application window.</summary>
public sealed record AppWindowSnapshot(
    IntPtr Handle,
    int ProcessId,
    string? ProcessPath,
    string? AppUserModelId,
    string Title,
    string DisplayName);

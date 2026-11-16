using OpenBar.Domain.Dock;

namespace OpenBar.Application.Dock;

/// <summary>One rendered dock entry (pinned or currently running).</summary>
public sealed record DockItemInfo(
    Guid? PinnedId,
    string Key,
    string DisplayName,
    string? LaunchTarget,
    string? LaunchArguments,
    PinnedTargetKind TargetKind,
    string? IconPath,
    bool IsPinned,
    bool IsRunning,
    IntPtr? WindowHandle,
    int SortOrder);

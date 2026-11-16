namespace OpenBar.Domain.Dock;

/// <summary>
/// A user-pinned application shown in the dock. <see cref="Target"/> is the
/// launch identity (executable, shortcut, AUMID or protocol). Resolved metadata
/// is cached for running-window matching but never replaces the launch target.
/// </summary>
public sealed class PinnedApplication
{
    public const int MaxNameLength = 200;
    public const int MaxTargetLength = 2_048;
    public const int MaxArgumentsLength = 2_048;
    public const int MaxResolvedTargetLength = 2_048;
    public const int MaxAppUserModelIdLength = 300;
    public const int MaxIconKeyLength = 2_048;

    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Launch target: exe path, .lnk path, AUMID or protocol.</summary>
    public string Target { get; set; } = string.Empty;

    public string? Arguments { get; set; }

    public PinnedTargetKind TargetKind { get; set; } = PinnedTargetKind.Executable;

    /// <summary>Resolved executable path from a shortcut, when available.</summary>
    public string? ResolvedTarget { get; set; }

    /// <summary>AppUserModelId for packaged/UWP applications, when available.</summary>
    public string? AppUserModelId { get; set; }

    /// <summary>Stable icon source key (usually the target path); no downloaded icons.</summary>
    public string? IconKey { get; set; }

    /// <summary>Zero-based position inside the pinned section.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

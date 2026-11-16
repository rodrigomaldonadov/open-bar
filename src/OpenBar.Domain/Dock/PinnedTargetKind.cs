namespace OpenBar.Domain.Dock;

/// <summary>How a pinned dock item is launched.</summary>
public enum PinnedTargetKind
{
    /// <summary>A direct executable path.</summary>
    Executable = 0,

    /// <summary>A Windows shortcut (.lnk) launched through the shell.</summary>
    Shortcut = 1,

    /// <summary>An AppUserModelId activated through the shell/activation manager.</summary>
    PackagedApp = 2,

    /// <summary>A shell protocol or URI (for example <c>ms-settings:</c>).</summary>
    Protocol = 3
}

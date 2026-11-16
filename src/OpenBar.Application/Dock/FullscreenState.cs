namespace OpenBar.Application.Dock;

/// <summary>How a foreground application occupies its monitor.</summary>
public enum FullscreenKind
{
    None = 0,

    /// <summary>D3D/presentation mode reported by the shell.</summary>
    Exclusive = 1,

    /// <summary>A borderless window covers the entire monitor rather than only the work area.</summary>
    Borderless = 2,

    /// <summary>The foreground belongs to OpenBar; preserve the previous fullscreen state.</summary>
    Unknown = 3
}

public readonly record struct FullscreenState(FullscreenKind Kind)
{
    public bool IsFullscreen => Kind != FullscreenKind.None;
}

/// <summary>Detects fullscreen applications so the dock can stay out of their way.</summary>
public interface IFullscreenStateService
{
    FullscreenState GetState();
}

namespace OpenBar.Domain.Widgets;

/// <summary>How a persisted widget instance is presented.</summary>
public enum WidgetPresentationMode
{
    /// <summary>Independent WinUI window, the Foundation 0.3-0.6 behaviour.</summary>
    FloatingWindow = 0,

    /// <summary>Compact surface hosted by the dock.</summary>
    DockEmbedded = 1
}

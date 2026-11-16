namespace OpenBar.WidgetRuntime;

/// <summary>Explicit runtime state of a widget instance.</summary>
public enum WidgetInstanceState
{
    /// <summary>Persisted but not initialized in this process.</summary>
    Created,

    Initializing,

    Initialized,

    Starting,

    Running,

    Stopping,

    /// <summary>Stopped and released; it can be started again.</summary>
    Stopped,

    /// <summary>An operation failed. The persisted configuration is preserved.</summary>
    Faulted,

    Disposed,

    /// <summary>
    /// Persisted instance whose widget definition is not available. It is
    /// reported but never deleted automatically.
    /// </summary>
    Unavailable
}

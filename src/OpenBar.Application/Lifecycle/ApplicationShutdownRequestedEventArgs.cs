namespace OpenBar.Application.Lifecycle;

/// <summary>
/// Raised exactly once when a controlled shutdown is requested (for example
/// from the system tray). The UI layer is responsible for actually stopping
/// the host and closing the window.
/// </summary>
public sealed class ApplicationShutdownRequestedEventArgs : EventArgs
{
    public ApplicationShutdownRequestedEventArgs(string reason)
    {
        Reason = reason;
    }

    public string Reason { get; }
}

namespace OpenBar.Application.Windows;

/// <summary>
/// Platform-neutral single-instance contract. The Windows implementation uses a
/// named mutex plus a per-user named pipe for activation requests.
/// </summary>
public interface ISingleInstanceService
{
    /// <summary>
    /// Raised when a second process asks this instance to come to the
    /// foreground. Handlers may be invoked from a background thread.
    /// </summary>
    event EventHandler? ActivationRequested;

    /// <summary>
    /// Attempts to become the primary instance. Returns <c>true</c> when this
    /// process created the single-instance mutex.
    /// </summary>
    bool TryAcquire();

    /// <summary>
    /// Sends an activation request to the existing instance, retrying while the
    /// existing process finishes starting. Returns <c>false</c> if no instance
    /// accepted the request within <paramref name="timeout"/>.
    /// </summary>
    Task<bool> NotifyExistingInstanceAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

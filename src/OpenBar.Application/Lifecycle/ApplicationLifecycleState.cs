namespace OpenBar.Application.Lifecycle;

/// <summary>
/// High-level state of the application host. The state machine is deliberately
/// platform neutral so it can be unit tested without a Windows message loop.
/// </summary>
public enum ApplicationLifecycleState
{
    Starting,
    Running,
    ShuttingDown,
    Stopped
}

namespace OpenBar.WidgetRuntime;

/// <summary>Raised when a lifecycle operation is not valid in the current state.</summary>
public sealed class WidgetStateException : InvalidOperationException
{
    public WidgetStateException(Guid instanceId, WidgetInstanceState currentState, string operation)
        : base($"Cannot {operation} widget instance {instanceId} while it is {currentState}.")
    {
        InstanceId = instanceId;
        CurrentState = currentState;
    }

    public Guid InstanceId { get; }

    public WidgetInstanceState CurrentState { get; }
}

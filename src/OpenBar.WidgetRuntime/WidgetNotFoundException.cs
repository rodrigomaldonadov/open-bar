namespace OpenBar.WidgetRuntime;

/// <summary>Raised when a widget instance id is not known to the runtime.</summary>
public sealed class WidgetNotFoundException : KeyNotFoundException
{
    public WidgetNotFoundException(Guid instanceId)
        : base($"Widget instance {instanceId} was not found.")
    {
        InstanceId = instanceId;
    }

    public Guid InstanceId { get; }
}

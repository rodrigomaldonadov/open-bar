namespace OpenBar.WidgetRuntime;

/// <summary>
/// Controlled lifecycle failure. The persisted instance is preserved unless the
/// caller explicitly deletes it after stopping successfully.
/// </summary>
public sealed class WidgetLifecycleException : Exception
{
    public WidgetLifecycleException(Guid? instanceId, string operation, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        InstanceId = instanceId;
        Operation = operation;
    }

    public Guid? InstanceId { get; }

    public string Operation { get; }
}

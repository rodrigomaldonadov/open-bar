namespace OpenBar.WidgetRuntime;

/// <summary>Raised when a widget exceeds a scheduler limit.</summary>
public sealed class WidgetSchedulerException : InvalidOperationException
{
    public WidgetSchedulerException(string message)
        : base(message)
    {
    }
}

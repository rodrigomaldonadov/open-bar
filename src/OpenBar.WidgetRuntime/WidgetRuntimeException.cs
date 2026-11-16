namespace OpenBar.WidgetRuntime;

/// <summary>Global widget-runtime failure (for example the instance store is unavailable).</summary>
public sealed class WidgetRuntimeException : Exception
{
    public WidgetRuntimeException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

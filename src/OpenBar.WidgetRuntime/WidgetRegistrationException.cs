namespace OpenBar.WidgetRuntime;

/// <summary>Raised when a definition cannot be registered (for example a duplicate id).</summary>
public sealed class WidgetRegistrationException : InvalidOperationException
{
    public WidgetRegistrationException(string widgetId, string message)
        : base(message)
    {
        WidgetId = widgetId;
    }

    public string WidgetId { get; }
}

namespace OpenBar.WidgetRuntime;

/// <summary>Raised when a widget definition is structurally invalid.</summary>
public sealed class WidgetDefinitionValidationException : ArgumentException
{
    public WidgetDefinitionValidationException(string propertyName, string message)
        : base(message, propertyName)
    {
        PropertyName = propertyName;
    }

    public string PropertyName { get; }
}

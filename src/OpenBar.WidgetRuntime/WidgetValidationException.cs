namespace OpenBar.WidgetRuntime;

/// <summary>Raised when a create or settings request is invalid before touching persistence.</summary>
public sealed class WidgetValidationException : ArgumentException
{
    public WidgetValidationException(string message, string? parameterName = null)
        : base(message, parameterName)
    {
    }
}

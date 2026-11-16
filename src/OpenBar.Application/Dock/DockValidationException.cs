namespace OpenBar.Application.Dock;

/// <summary>Raised when a dock operation is invalid before touching persistence.</summary>
public sealed class DockValidationException : InvalidOperationException
{
    public DockValidationException(string message)
        : base(message)
    {
    }
}

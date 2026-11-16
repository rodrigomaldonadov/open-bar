namespace OpenBar.Application.Windows;

/// <summary>
/// Abstraction over the main dock window so tray commands, lifecycle code and
/// tests do not depend on WinUI directly.
/// </summary>
public interface IWindowManagementService
{
    bool IsVisible { get; }

    void Show();

    void Hide();

    void ToggleVisibility();

    void ShowAndActivate();
}

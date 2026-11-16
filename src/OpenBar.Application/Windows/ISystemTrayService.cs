namespace OpenBar.Application.Windows;

/// <summary>
/// Abstraction for the notification-area icon. The Windows implementation lives
/// in Infrastructure and uses Shell_NotifyIcon directly.
/// </summary>
public interface ISystemTrayService : IDisposable
{
    bool IsVisible { get; }

    void Show();

    void Hide();
}

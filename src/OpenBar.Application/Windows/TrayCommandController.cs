using Microsoft.Extensions.Logging;
using OpenBar.Application.Lifecycle;

namespace OpenBar.Application.Windows;

/// <summary>
/// Maps system tray commands to the window and lifecycle abstractions. This is
/// intentionally tiny and platform neutral so the command behavior can be
/// unit tested without a graphical session.
/// </summary>
public sealed partial class TrayCommandController
{
    private readonly IWindowManagementService _windows;
    private readonly IApplicationLifecycle _lifecycle;
    private readonly ILogger<TrayCommandController> _logger;

    public TrayCommandController(
        IWindowManagementService windows,
        IApplicationLifecycle lifecycle,
        ILogger<TrayCommandController> logger)
    {
        _windows = windows;
        _lifecycle = lifecycle;
        _logger = logger;
    }

    public void OpenDock()
    {
        LogOpenDock(_logger);
        _windows.ShowAndActivate();
    }

    public void ToggleDock()
    {
        LogToggleDock(_logger);
        _windows.ToggleVisibility();
    }

    public void ExitApplication()
    {
        LogExitApplication(_logger);
        _lifecycle.RequestShutdown("SystemTrayExit");
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Information, Message = "Tray command: open OpenBar dock.")]
    private static partial void LogOpenDock(ILogger<TrayCommandController> logger);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Information, Message = "Tray command: toggle OpenBar dock visibility.")]
    private static partial void LogToggleDock(ILogger<TrayCommandController> logger);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information, Message = "Tray command: exit OpenBar.")]
    private static partial void LogExitApplication(ILogger<TrayCommandController> logger);
}

using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenBar.Application.Lifecycle;
using OpenBar.Application.Windows;
using OpenBar.Dock;
using OpenBar.Domain.Layouts;
using OpenBar.Presentation.Dock;
using Windows.Graphics;
using WinRT.Interop;

namespace OpenBar.App.Services;

/// <summary>
/// Owns the dock window: borderless topmost tool window, monitor/DPI-aware
/// placement, auto-hide and controlled hide/close policy. It also keeps the
/// existing tray/single-instance window contract.
/// </summary>
public sealed partial class DockWindowService : IWindowManagementService, IDisposable
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpFrameChanged = 0x0020;

    private readonly DockWindow _window;
    private readonly DockViewModel _viewModel;
    private readonly IApplicationLifecycle _lifecycle;
    private readonly DockAutoHideController _autoHide;
    private readonly ILogger<DockWindowService> _logger;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _autoHideTimer;
    private readonly IntPtr _windowHandle;

    private DisplayAreaWatcher? _displayWatcher;
    private DockRect _dockBounds;
    private DockRect _workArea;
    private bool _autoHideTimerRunning;
    private bool _shutdownClose;
    private bool _minimizedToTray;
    private bool _hidden;
    private bool _disposed;

    public DockWindowService(
        DockWindow window,
        DockViewModel viewModel,
        IApplicationLifecycle lifecycle,
        DockAutoHideController autoHide,
        ILogger<DockWindowService> logger)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(autoHide);

        _window = window;
        _viewModel = viewModel;
        _lifecycle = lifecycle;
        _autoHide = autoHide;
        _logger = logger;
        _windowHandle = WindowNative.GetWindowHandle(window);
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        ConfigureWindow();

        _autoHideTimer = _dispatcherQueue.CreateTimer();
        _autoHideTimer.Interval = TimeSpan.FromMilliseconds(120);
        _autoHideTimer.IsRepeating = true;
        _autoHideTimer.Tick += OnAutoHideTick;

        _window.AppWindow.Closing += OnWindowClosing;
        _window.AppWindow.Changed += OnWindowChanged;
        _viewModel.LayoutChanged += OnLayoutChanged;
        _autoHide.Enabled = _viewModel.AutoHide;

        TryStartDisplayWatcher();
        ApplyLayout();

        if (_viewModel.AutoHide)
        {
            StartAutoHideTimer();
        }
    }

    public bool IsVisible => !_hidden && _window.AppWindow.IsVisible;

    public void Show()
    {
        if (_disposed)
        {
            return;
        }

        _autoHide.Reset(hidden: false);
        ShowDock(activate: false);
        ApplyLayout();
    }

    public void Hide()
    {
        if (_disposed)
        {
            return;
        }

        HideDock();
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            ShowAndActivate();
        }
    }

    public void ShowAndActivate()
    {
        if (_disposed)
        {
            return;
        }

        _autoHide.Reset(hidden: false);
        ShowDock(activate: true);
        ApplyLayout();
        _window.Activate();
    }

    /// <summary>Called by the app during controlled shutdown.</summary>
    public void CloseForShutdown()
    {
        if (_disposed)
        {
            return;
        }

        _shutdownClose = true;
        StopAutoHideTimer();
        StopDisplayWatcher();

        try
        {
            _window.Close();
        }
        catch (Exception ex)
        {
            LogCloseForShutdownFailed(_logger, ex);
        }
    }

    private void ConfigureWindow()
    {
        try
        {
            if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsAlwaysOnTop = true;
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            // Keep the dock out of Alt+Tab and the taskbar through the supported
            // AppWindow property (no WS_EX_TOOLWINDOW hack).
            _window.AppWindow.IsShownInSwitchers = false;

            // IsShownInSwitchers does not remove the taskbar button for an
            // unpackaged WinUI window on Windows 11; the documented tool-window
            // extended style is still required.
            var extendedStyle = GetWindowLongPtr(_windowHandle, GwlExStyle).ToInt64();
            extendedStyle |= WsExToolWindow;
            extendedStyle &= ~WsExAppWindow;
            _ = SetWindowLongPtr(_windowHandle, GwlExStyle, new IntPtr(extendedStyle));
            _ = SetWindowPos(
                _windowHandle,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }
        catch (Exception ex)
        {
            LogWindowConfigurationFailed(_logger, ex);
        }

        var corner = DwmwcpRound;
        _ = DwmSetWindowAttribute(
            _windowHandle,
            DwmwaWindowCornerPreference,
            ref corner,
            sizeof(int));
    }

    private void ApplyLayout()
    {
        if (_disposed)
        {
            return;
        }

        if (!_dispatcherQueue.HasThreadAccess)
        {
            _ = _dispatcherQueue.TryEnqueue(ApplyLayout);
            return;
        }

        var monitors = DisplayArea.FindAll();
        if (monitors.Count == 0)
        {
            return;
        }

        var display = ResolveDisplay(monitors);
        var work = display.WorkArea;
        _workArea = new DockRect(work.X, work.Y, work.Width, work.Height);

        var layout = new DockLayout
        {
            Position = _viewModel.Position,
            Alignment = _viewModel.Alignment,
            IconSize = _viewModel.IconSize,
            Spacing = _viewModel.Spacing,
            Offset = _viewModel.Offset
        };

        var metrics = DockLayoutCalculator.Calculate(
            layout,
            _viewModel.PinnedCount,
            _viewModel.RunningCount);

        var dpi = GetDpiForWindow(_windowHandle);
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        _dockBounds = DockLayoutCalculator.CalculateBounds(layout, metrics, _workArea, scale);

        _autoHide.Enabled = _viewModel.AutoHide;

        if (_viewModel.AutoHide)
        {
            StartAutoHideTimer();
        }
        else
        {
            StopAutoHideTimer();

            if (_hidden)
            {
                ShowDock(activate: false);
            }
        }

        if (_hidden)
        {
            return;
        }

        MoveDock();
    }

    private DisplayArea ResolveDisplay(IReadOnlyList<DisplayArea> monitors)
    {
        var infos = new DockMonitorInfo[monitors.Count];

        for (var index = 0; index < monitors.Count; index++)
        {
            var display = monitors[index];
            infos[index] = new DockMonitorInfo(
                display.DisplayId.Value.ToString(CultureInfo.InvariantCulture),
                display.IsPrimary,
                ToDockRect(display.WorkArea));
        }

        var target = DockMonitorResolver.Select(_viewModel.MonitorTarget, infos);
        if (target is not null)
        {
            for (var index = 0; index < monitors.Count; index++)
            {
                if (monitors[index].DisplayId.Value.ToString(CultureInfo.InvariantCulture) == target.Id)
                {
                    return monitors[index];
                }
            }
        }

        return DisplayArea.Primary ?? monitors[0];
    }

    private void MoveDock()
    {
        try
        {
            _window.AppWindow.MoveAndResize(new RectInt32(
                _dockBounds.Left,
                _dockBounds.Top,
                _dockBounds.Width,
                _dockBounds.Height));
        }
        catch (Exception ex)
        {
            LogMoveFailed(_logger, ex);
        }
    }

    private void ShowDock(bool activate)
    {
        _hidden = false;

        try
        {
            _window.AppWindow.Show(activate);
            _viewModel.SetActive(true);
            LogShown(_logger);

            if (_viewModel.AutoHide)
            {
                StartAutoHideTimer();
            }
        }
        catch (Exception ex)
        {
            LogShowFailed(_logger, ex);
        }
    }

    private void HideDock()
    {
        _hidden = true;
        _autoHide.Reset(hidden: true);

        try
        {
            _window.AppWindow.Hide();
            _viewModel.SetActive(false);
            LogHidden(_logger);
        }
        catch (Exception ex)
        {
            LogHideFailed(_logger, ex);
        }

        if (!_viewModel.AutoHide)
        {
            StopAutoHideTimer();
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e) => ApplyLayout();

    private void OnAutoHideTick(DispatcherQueueTimer sender, object args)
    {
        if (_disposed || !_viewModel.AutoHide || !GetCursorPos(out var point))
        {
            return;
        }

        var action = _autoHide.Update(
            _dockBounds,
            _workArea,
            _viewModel.Position,
            new DockPoint(point.X, point.Y),
            DateTimeOffset.UtcNow);

        if (action == AutoHideAction.Show && _hidden)
        {
            ShowDock(activate: false);
            LogAutoHideShown(_logger);
        }
        else if (action == AutoHideAction.Hide && !_hidden)
        {
            HideDock();
            LogAutoHideHidden(_logger);
        }
    }

    private void StartAutoHideTimer()
    {
        if (_autoHideTimerRunning || _disposed)
        {
            return;
        }

        _autoHideTimer.Start();
        _autoHideTimerRunning = true;
    }

    private void StopAutoHideTimer()
    {
        if (!_autoHideTimerRunning)
        {
            return;
        }

        _autoHideTimer.Stop();
        _autoHideTimerRunning = false;
    }

    private void TryStartDisplayWatcher()
    {
        try
        {
            _displayWatcher = DisplayArea.CreateWatcher();
            _displayWatcher.Updated += OnDisplayAreasUpdated;
            _displayWatcher.Start();
        }
        catch (Exception ex)
        {
            LogDisplayWatcherFailed(_logger, ex);
        }
    }

    private void OnDisplayAreasUpdated(DisplayAreaWatcher sender, object args)
        => _ = _dispatcherQueue.TryEnqueue(ApplyLayout);

    private void StopDisplayWatcher()
    {
        if (_displayWatcher is null)
        {
            return;
        }

        try
        {
            _displayWatcher.Updated -= OnDisplayAreasUpdated;
            _displayWatcher.Stop();
        }
        catch (Exception ex)
        {
            LogDisplayWatcherStopFailed(_logger, ex);
        }
        finally
        {
            _displayWatcher = null;
        }
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownClose || _lifecycle.IsShuttingDown)
        {
            return;
        }

        args.Cancel = true;
        LogCloseRequestedHiding(_logger);
        HideDock();
    }

    private void OnWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_disposed || _shutdownClose || _hidden)
        {
            return;
        }

        try
        {
            if (!IsIconic(_windowHandle))
            {
                _minimizedToTray = false;
                return;
            }

            if (_minimizedToTray)
            {
                return;
            }

            _minimizedToTray = true;
            LogMinimizedToTray(_logger);
            HideDock();
        }
        catch (Exception ex)
        {
            LogMinimizeToTrayFailed(_logger, ex);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAutoHideTimer();
        StopDisplayWatcher();

        try
        {
            _window.AppWindow.Closing -= OnWindowClosing;
            _window.AppWindow.Changed -= OnWindowChanged;
            _viewModel.LayoutChanged -= OnLayoutChanged;
        }
        catch (Exception ex)
        {
            LogEventsAlreadyDetached(_logger, ex);
        }
    }

    private static DockRect ToDockRect(RectInt32 rect)
        => new(rect.X, rect.Y, rect.Width, rect.Height);

    [LoggerMessage(EventId = 8101, Level = LogLevel.Information, Message = "Dock shown.")]
    private static partial void LogShown(ILogger<DockWindowService> logger);

    [LoggerMessage(EventId = 8102, Level = LogLevel.Error, Message = "Failed to show the dock window.")]
    private static partial void LogShowFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8103, Level = LogLevel.Information, Message = "Dock hidden.")]
    private static partial void LogHidden(ILogger<DockWindowService> logger);

    [LoggerMessage(EventId = 8104, Level = LogLevel.Error, Message = "Failed to hide the dock window.")]
    private static partial void LogHideFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8105, Level = LogLevel.Information, Message = "Dock window close requested; hiding instead of exiting.")]
    private static partial void LogCloseRequestedHiding(ILogger<DockWindowService> logger);

    [LoggerMessage(EventId = 8106, Level = LogLevel.Information, Message = "Dock minimized; hiding it.")]
    private static partial void LogMinimizedToTray(ILogger<DockWindowService> logger);

    [LoggerMessage(EventId = 8107, Level = LogLevel.Error, Message = "Failed to hide the minimized dock.")]
    private static partial void LogMinimizeToTrayFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8108, Level = LogLevel.Error, Message = "Failed to configure the dock window presenter.")]
    private static partial void LogWindowConfigurationFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8109, Level = LogLevel.Error, Message = "Failed to move/resize the dock window.")]
    private static partial void LogMoveFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8110, Level = LogLevel.Error, Message = "Failed to close the dock during shutdown.")]
    private static partial void LogCloseForShutdownFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8111, Level = LogLevel.Debug, Message = "Dock auto-hidden.")]
    private static partial void LogAutoHideHidden(ILogger<DockWindowService> logger);

    [LoggerMessage(EventId = 8112, Level = LogLevel.Debug, Message = "Dock revealed from auto-hide.")]
    private static partial void LogAutoHideShown(ILogger<DockWindowService> logger);

    [LoggerMessage(EventId = 8113, Level = LogLevel.Warning, Message = "Could not start the display-area watcher.")]
    private static partial void LogDisplayWatcherFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8114, Level = LogLevel.Debug, Message = "Could not stop the display-area watcher.")]
    private static partial void LogDisplayWatcherStopFailed(ILogger<DockWindowService> logger, Exception exception);

    [LoggerMessage(EventId = 8115, Level = LogLevel.Debug, Message = "Dock window events were already detached during disposal.")]
    private static partial void LogEventsAlreadyDetached(ILogger<DockWindowService> logger, Exception exception);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}

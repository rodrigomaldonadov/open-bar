using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenBar.Application.Windows;

namespace OpenBar.Infrastructure.Windows.SystemTray;

/// <summary>
/// Win32 notification-area implementation.
///
/// The service creates a hidden top-level window (not a message-only window,
/// because message-only windows do not receive the shell's <c>TaskbarCreated</c>
/// broadcast). The WinUI message loop dispatches messages to the window
/// procedure, including tray callbacks. Start/Stop are synchronous so they run
/// on the UI thread that owns the message loop.
///
/// No WinForms dependency is used.
/// </summary>
public sealed partial class WindowsSystemTrayService : ISystemTrayService, IHostedService, IDisposable
{
    private const uint WM_APP = 0x8000;
    private const uint WM_TRAY_CALLBACK = WM_APP + 1;
    private const uint WM_LBUTTONDBLCLK = 0x0203;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_NULL = 0x0000;
    private const uint WM_DESTROY = 0x0002;

    private const uint NIM_ADD = 0x0000;
    private const uint NIM_MODIFY = 0x0001;
    private const uint NIM_DELETE = 0x0002;

    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;

    private const uint MF_STRING = 0x0000;
    private const uint MF_SEPARATOR = 0x0800;

    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_TOPALIGN = 0x0000;
    private const uint TPM_RIGHTBUTTON = 0x0002;
    private const uint TPM_RETURNCMD = 0x0100;

    private const uint IMAGE_ICON = 0x0001;
    private const uint LR_LOADFROMFILE = 0x0010;
    private const uint IDI_APPLICATION = 32512;
    private const int SM_CXSMICON = 49;
    private const int SM_CYSMICON = 50;

    private const uint ID_TRAY_OPEN = 1001;
    private const uint ID_TRAY_TOGGLE = 1002;
    private const uint ID_TRAY_EXIT = 1003;

    private const int ErrorClassAlreadyExists = 1410;

    private readonly TrayCommandController _commands;
    private readonly TrayIconOptions _options;
    private readonly ILogger<WindowsSystemTrayService> _logger;
    private readonly string _className = $"OpenBar.TrayWindow.{Guid.NewGuid():N}";
    private readonly uint _taskbarCreatedMessage;

    private WndProc? _wndProc;
    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private bool _ownsIcon;
    private bool _classRegistered;
    private bool _iconAdded;
    private bool _disposed;
    private int _creatorThreadId;

    public WindowsSystemTrayService(
        TrayCommandController commands,
        TrayIconOptions options,
        ILogger<WindowsSystemTrayService> logger)
    {
        _commands = commands;
        _options = options;
        _logger = logger;
        _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
    }

    public bool IsVisible => _iconAdded;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            Show();
        }
        catch (Exception ex)
        {
            // A tray failure must not prevent the dock from opening. The error
            // is logged and the user can still use the window.
            LogTrayCreateFailed(_logger, ex);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            RemoveIcon();
            DestroyTrayWindow();
        }
        catch (Exception ex)
        {
            LogTrayRemoveFailed(_logger, ex);
        }

        return Task.CompletedTask;
    }

    public void Show()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        EnsureTrayWindow();

        if (_hIcon == IntPtr.Zero)
        {
            _hIcon = LoadTrayIcon();
        }

        AddIcon();
    }

    public void Hide()
    {
        RemoveIcon();
    }

    private void EnsureTrayWindow()
    {
        if (_hwnd != IntPtr.Zero)
        {
            return;
        }

        _creatorThreadId = Environment.CurrentManagedThreadId;
        _wndProc = TrayWindowProc;

        var hInstance = GetModuleHandle(null);
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = hInstance,
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = _className,
            hIconSm = IntPtr.Zero
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorClassAlreadyExists)
            {
                throw new Win32Exception(error, "RegisterClassEx failed for the tray window.");
            }

            LogTrayClassReused(_logger, error);
        }
        else
        {
            _classRegistered = true;
        }

        _hwnd = CreateWindowEx(
            0,
            _className,
            "OpenBar.TrayWindow",
            0,
            0,
            0,
            0,
            0,
            IntPtr.Zero,
            IntPtr.Zero,
            hInstance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            var error = Marshal.GetLastWin32Error();
            if (_classRegistered)
            {
                UnregisterClass(_className, hInstance);
                _classRegistered = false;
            }

            throw new Win32Exception(error, "CreateWindowEx failed for the tray window.");
        }

        LogTrayWindowCreated(_logger, _hwnd);
    }

    private void AddIcon()
    {
        if (_hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("The tray window has not been created.");
        }

        var data = CreateNotifyIconData();

        if (!Shell_NotifyIcon(NIM_ADD, ref data))
        {
            // After an Explorer restart the icon may still be registered from
            // the shell's point of view; try to modify it before giving up.
            var firstError = Marshal.GetLastWin32Error();
            if (Shell_NotifyIcon(NIM_MODIFY, ref data))
            {
                _iconAdded = true;
                return;
            }

            _iconAdded = false;
            LogTrayIconAddFailed(_logger, firstError);
            return;
        }

        _iconAdded = true;
        LogTrayIconAdded(_logger);
    }

    private void RemoveIcon()
    {
        if (_iconAdded && _hwnd != IntPtr.Zero)
        {
            var data = CreateNotifyIconData();
            if (!Shell_NotifyIcon(NIM_DELETE, ref data))
            {
                LogTrayIconRemoveFailed(_logger, Marshal.GetLastWin32Error());
            }
        }

        _iconAdded = false;

        if (_hIcon != IntPtr.Zero && _ownsIcon)
        {
            DestroyIcon(_hIcon);
        }

        _hIcon = IntPtr.Zero;
        _ownsIcon = false;
    }

    private IntPtr LoadTrayIcon()
    {
        var path = _options.IconPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var cx = GetSystemMetrics(SM_CXSMICON);
            var cy = GetSystemMetrics(SM_CYSMICON);
            var icon = LoadImage(IntPtr.Zero, path, IMAGE_ICON, cx, cy, LR_LOADFROMFILE);
            if (icon != IntPtr.Zero)
            {
                _ownsIcon = true;
                return icon;
            }

            LogTrayIconLoadFailed(_logger, path, Marshal.GetLastWin32Error());
        }
        else
        {
            LogTrayIconFileMissing(_logger, path);
        }

        _ownsIcon = false;
        return LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
    }

    private NOTIFYICONDATA CreateNotifyIconData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
            uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            uCallbackMessage = WM_TRAY_CALLBACK,
            hIcon = _hIcon,
            szTip = Truncate(_options.ToolTip, 127),
            dwState = 0,
            dwStateMask = 0,
            szInfo = string.Empty,
            uTimeoutOrVersion = 0,
            szInfoTitle = string.Empty,
            dwInfoFlags = 0,
            guidItem = Guid.Empty,
            hBalloonIcon = IntPtr.Zero
        };
    }

    private IntPtr TrayWindowProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (_taskbarCreatedMessage != 0 && message == _taskbarCreatedMessage)
            {
                if (!_disposed)
                {
                    LogTaskbarCreated(_logger);
                    AddIcon();
                }

                return IntPtr.Zero;
            }

            if (message == WM_TRAY_CALLBACK)
            {
                var notification = (uint)(lParam.ToInt64() & 0xFFFF);
                switch (notification)
                {
                    case WM_LBUTTONDBLCLK:
                        _commands.ToggleDock();
                        return IntPtr.Zero;

                    case WM_RBUTTONUP:
                        ShowContextMenu();
                        return IntPtr.Zero;
                }
            }

            if (message == WM_DESTROY)
            {
                // Never post WM_QUIT here: the WinUI loop owns the process
                // lifetime, not this hidden window.
                return IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            LogTrayWindowProcFailed(_logger, ex);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
        {
            LogTrayMenuCreateFailed(_logger);
            return;
        }

        try
        {
            AppendMenu(menu, MF_STRING, (UIntPtr)ID_TRAY_OPEN, "Abrir OpenBar");
            AppendMenu(menu, MF_STRING, (UIntPtr)ID_TRAY_TOGGLE, "Mostrar u Ocultar Dock");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)ID_TRAY_EXIT, "Salir de OpenBar");

            GetCursorPos(out var point);

            // Required so the popup menu dismisses correctly when the user
            // clicks elsewhere.
            SetForegroundWindow(_hwnd);

            var command = TrackPopupMenuEx(
                menu,
                TPM_LEFTALIGN | TPM_TOPALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
                point.X,
                point.Y,
                _hwnd,
                IntPtr.Zero);

            PostMessage(_hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero);

            switch (command)
            {
                case ID_TRAY_OPEN:
                    _commands.OpenDock();
                    break;

                case ID_TRAY_TOGGLE:
                    _commands.ToggleDock();
                    break;

                case ID_TRAY_EXIT:
                    _commands.ExitApplication();
                    break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    private void DestroyTrayWindow()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        if (Environment.CurrentManagedThreadId != _creatorThreadId)
        {
            // DestroyWindow has thread affinity. In normal shutdown StopAsync
            // runs on the UI thread; if that guarantee is ever broken we leave
            // the hidden window to be reclaimed when the process exits instead
            // of corrupting the window manager state.
            LogTrayWindowCrossThreadDestroy(_logger);
            return;
        }

        var hInstance = GetModuleHandle(null);
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;

        if (_classRegistered)
        {
            UnregisterClass(_className, hInstance);
            _classRegistered = false;
        }

        _wndProc = null;
        LogTrayWindowDestroyed(_logger);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            RemoveIcon();
            DestroyTrayWindow();
        }
        catch (Exception ex)
        {
            LogTrayDisposeFailed(_logger, ex);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "OpenBar";
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Error, Message = "The system tray icon could not be created.")]
    private static partial void LogTrayCreateFailed(ILogger<WindowsSystemTrayService> logger, Exception exception);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Error, Message = "The system tray icon could not be removed cleanly.")]
    private static partial void LogTrayRemoveFailed(ILogger<WindowsSystemTrayService> logger, Exception exception);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "Tray window class already existed; reusing it. Win32 error: {Error}.")]
    private static partial void LogTrayClassReused(ILogger<WindowsSystemTrayService> logger, int error);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Information, Message = "Hidden tray window created (HWND {Handle}).")]
    private static partial void LogTrayWindowCreated(ILogger<WindowsSystemTrayService> logger, IntPtr handle);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning, Message = "Shell_NotifyIcon could not add the tray icon (error {Error}). It will be retried after TaskbarCreated.")]
    private static partial void LogTrayIconAddFailed(ILogger<WindowsSystemTrayService> logger, int error);

    [LoggerMessage(EventId = 4006, Level = LogLevel.Information, Message = "System tray icon added.")]
    private static partial void LogTrayIconAdded(ILogger<WindowsSystemTrayService> logger);

    [LoggerMessage(EventId = 4007, Level = LogLevel.Warning, Message = "Shell_NotifyIcon could not remove the tray icon (error {Error}).")]
    private static partial void LogTrayIconRemoveFailed(ILogger<WindowsSystemTrayService> logger, int error);

    [LoggerMessage(EventId = 4008, Level = LogLevel.Warning, Message = "LoadImage failed for tray icon {IconPath} (error {Error}); using the system fallback icon.")]
    private static partial void LogTrayIconLoadFailed(ILogger<WindowsSystemTrayService> logger, string iconPath, int error);

    [LoggerMessage(EventId = 4009, Level = LogLevel.Warning, Message = "Tray icon file was not found at {IconPath}; using the system fallback icon.")]
    private static partial void LogTrayIconFileMissing(ILogger<WindowsSystemTrayService> logger, string iconPath);

    [LoggerMessage(EventId = 4010, Level = LogLevel.Information, Message = "TaskbarCreated received; restoring the tray icon.")]
    private static partial void LogTaskbarCreated(ILogger<WindowsSystemTrayService> logger);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Error, Message = "The system tray window procedure failed.")]
    private static partial void LogTrayWindowProcFailed(ILogger<WindowsSystemTrayService> logger, Exception exception);

    [LoggerMessage(EventId = 4012, Level = LogLevel.Warning, Message = "CreatePopupMenu failed for the tray context menu.")]
    private static partial void LogTrayMenuCreateFailed(ILogger<WindowsSystemTrayService> logger);

    [LoggerMessage(EventId = 4013, Level = LogLevel.Warning, Message = "The tray window must be destroyed on its creating UI thread; skipping cross-thread destroy.")]
    private static partial void LogTrayWindowCrossThreadDestroy(ILogger<WindowsSystemTrayService> logger);

    [LoggerMessage(EventId = 4014, Level = LogLevel.Information, Message = "Hidden tray window destroyed.")]
    private static partial void LogTrayWindowDestroyed(ILogger<WindowsSystemTrayService> logger);

    [LoggerMessage(EventId = 4015, Level = LogLevel.Error, Message = "The system tray service did not dispose cleanly.")]
    private static partial void LogTrayDisposeFailed(ILogger<WindowsSystemTrayService> logger, Exception exception);

    private delegate IntPtr WndProc(IntPtr hWnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszMenuName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszClassName;

        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;

        public uint dwState;
        public uint dwStateMask;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;

        public uint uTimeoutOrVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;

        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string? lpNewItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(
        IntPtr hMenu,
        uint uFlags,
        int x,
        int y,
        IntPtr hWnd,
        IntPtr lptpm);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImage(
        IntPtr hInst,
        string name,
        uint type,
        int cx,
        int cy,
        uint fuLoad);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}

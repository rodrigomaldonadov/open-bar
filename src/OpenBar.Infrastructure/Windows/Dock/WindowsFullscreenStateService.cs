using System.Runtime.InteropServices;
using System.Text;
using OpenBar.Application.Dock;

namespace OpenBar.Infrastructure.Windows.Dock;

/// <summary>
/// Best-effort fullscreen detection:
/// - shell notification state reports D3D/presentation fullscreen;
/// - a foreground borderless/popup window covering the whole monitor is
///   treated as borderless fullscreen.
/// Maximized windows cover only the work area and are not treated as
/// fullscreen. Custom-chrome apps that maximize without a caption can still be
/// misclassified; that limitation is intentional and documented.
/// </summary>
public sealed class WindowsFullscreenStateService : IFullscreenStateService
{
    private const int QunsRunningD3dFullScreen = 3;
    private const int QunsPresentationMode = 4;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const uint MonitorDefaultToNearest = 2;

    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW"
    };

    public FullscreenState GetState()
    {
        try
        {
            if (SHQueryUserNotificationState(out var state) == 0
                && (state == QunsRunningD3dFullScreen || state == QunsPresentationMode))
            {
                return new FullscreenState(FullscreenKind.Exclusive);
            }

            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || !IsWindowVisible(foreground))
            {
                return default;
            }

            _ = GetWindowThreadProcessId(foreground, out var processId);
            if (processId == 0 || processId == (uint)Environment.ProcessId)
            {
                // The dock/tools window may own the foreground while a
                // fullscreen app is behind it. Keep the previous state.
                return new FullscreenState(FullscreenKind.Unknown);
            }

            var className = new char[256];
            var classLength = GetClassName(foreground, className, className.Length);
            var windowClass = new string(className, 0, Math.Max(0, classLength));
            if (ShellClasses.Contains(windowClass))
            {
                return default;
            }

            if (!GetWindowRect(foreground, out var windowRect))
            {
                return default;
            }

            var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
            {
                return default;
            }

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return default;
            }

            var target = monitorInfo.rcMonitor;
            const int tolerance = 2;
            var coversMonitor = windowRect.Left <= target.Left + tolerance
                                && windowRect.Top <= target.Top + tolerance
                                && windowRect.Right >= target.Right - tolerance
                                && windowRect.Bottom >= target.Bottom - tolerance;

            if (!coversMonitor)
            {
                return default;
            }

            var style = GetWindowLongPtr(foreground, GwlStyle).ToInt64();
            var hasCaption = (style & WsCaption) != 0;

            return hasCaption
                ? default
                : new FullscreenState(FullscreenKind.Borderless);
        }
        catch (Exception)
        {
            // Detection is advisory: a failure must never break the dock.
            return default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO monitorInfo);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
}

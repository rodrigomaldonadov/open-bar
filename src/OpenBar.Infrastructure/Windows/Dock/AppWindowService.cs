using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using OpenBar.Application.Dock;

namespace OpenBar.Infrastructure.Windows.Dock;

/// <summary>
/// Enumerates visible top-level windows and identifies their owning process and
/// AppUserModelId. Runs off the UI thread; shell surfaces, background processes
/// without a visible window and OpenBar itself are excluded.
/// </summary>
public sealed partial class AppWindowService : IAppWindowService
{
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const int DwmwaCloaked = 14;
    private const uint ProcessQueryLimitedInformation = 0x1000;

    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Progman",
        "WorkerW",
        "Windows.UI.Core.CoreWindow",
        "ApplicationManager_DesktopShellWindow",
        "XamlExplorerHostIslandWindow",
        "TaskListThumbnailWnd",
        "ForegroundStaging"
    };

    public Task<IReadOnlyList<AppWindowSnapshot>> GetVisibleWindowsAsync(
        CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<AppWindowSnapshot>>(
            () => Enumerate(cancellationToken),
            cancellationToken);

    private static List<AppWindowSnapshot> Enumerate(CancellationToken cancellationToken)
    {
        var windows = new List<AppWindowSnapshot>();
        var ownProcessId = (uint)Environment.ProcessId;

        _ = EnumWindows((handle, _) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!IsWindowVisible(handle))
            {
                return true;
            }

            var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
            if ((style & WsExToolWindow) != 0 && (style & WsExAppWindow) == 0)
            {
                return true;
            }

            if (IsCloaked(handle))
            {
                return true;
            }

            var classNameBuffer = new char[256];
            var classNameLength = GetClassName(handle, classNameBuffer, classNameBuffer.Length);
            var className = new string(classNameBuffer, 0, Math.Max(0, classNameLength));
            if (IgnoredClasses.Contains(className))
            {
                return true;
            }

            var titleBuffer = new char[512];
            var titleLength = GetWindowText(handle, titleBuffer, titleBuffer.Length);
            var windowTitle = new string(titleBuffer, 0, Math.Max(0, titleLength)).Trim();
            if (windowTitle.Length == 0)
            {
                return true;
            }

            var threadId = GetWindowThreadProcessId(handle, out var processId);
            if (threadId == 0 || processId == 0 || processId == ownProcessId)
            {
                return true;
            }

            var processPath = GetProcessPath(processId);
            if (string.IsNullOrWhiteSpace(processPath))
            {
                return true;
            }

            windows.Add(new AppWindowSnapshot(
                handle,
                (int)processId,
                processPath,
                GetAppUserModelId(handle),
                windowTitle,
                GetDisplayName(processPath, windowTitle)));

            return true;
        }, IntPtr.Zero);

        return windows;
    }

    private static bool IsCloaked(IntPtr window)
        => DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    private static string? GetProcessPath(uint processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = new char[1_024];
            var length = buffer.Length;
            return QueryFullProcessImageName(handle, 0, buffer, ref length)
                ? new string(buffer, 0, Math.Max(0, length))
                : null;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static string GetDisplayName(string processPath, string windowTitle)
    {
        var fileName = Path.GetFileNameWithoutExtension(processPath);

        try
        {
            var description = FileVersionInfo.GetVersionInfo(processPath).FileDescription;
            if (!string.IsNullOrWhiteSpace(description)
                && !description.Equals("Application Frame Host", StringComparison.OrdinalIgnoreCase)
                && !description.Equals("Host de la aplicación", StringComparison.OrdinalIgnoreCase))
            {
                return description;
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (IOException)
        {
        }

        return string.Equals(fileName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)
            ? windowTitle
            : fileName;
    }

    private static string? GetAppUserModelId(IntPtr window)
    {
        try
        {
            var iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreForWindow(window, ref iid, out var store) != 0 || store is null)
            {
                return null;
            }

            try
            {
                var key = AppUserModelIdKey;
                store.GetValue(ref key, out var value);

                try
                {
                    return value.vt == 31 && value.pointer != IntPtr.Zero
                        ? Marshal.PtrToStringUni(value.pointer)
                        : null;
                }
                finally
                {
                    _ = PropVariantClear(ref value);
                }
            }
            finally
            {
                if (Marshal.IsComObject(store))
                {
                    _ = Marshal.FinalReleaseComObject(store);
                }
            }
        }
        catch (COMException)
        {
            return null;
        }
        catch (InvalidCastException)
        {
            return null;
        }
    }

    private static readonly PropertyKey AppUserModelIdKey = new()
    {
        FormatId = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        PropertyId = 5
    };

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, [Out] char[] text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process,
        int flags,
        [Out] char[] executableName,
        ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SHGetPropertyStoreForWindow(
        IntPtr hWnd,
        ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IPropertyStore? propertyStore);

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public int PropertyId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        public ushort reserved1;
        public ushort reserved2;
        public ushort reserved3;
        public IntPtr pointer;
        public int pointer2;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint propertyCount);

        void GetAt(uint propertyIndex, out PropertyKey key);

        void GetValue(ref PropertyKey key, out PropVariant value);

        void SetValue(ref PropertyKey key, ref PropVariant value);

        void Commit();
    }
}

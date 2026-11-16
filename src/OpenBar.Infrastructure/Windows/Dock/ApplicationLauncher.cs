using System.Diagnostics;
using System.Runtime.InteropServices;
using OpenBar.Application.Dock;

namespace OpenBar.Infrastructure.Windows.Dock;

/// <summary>Launches shell targets and brings existing application windows forward.</summary>
public sealed partial class ApplicationLauncher : IApplicationLauncher
{
    private const int SwRestore = 9;

    public Task LaunchAsync(DockItemInfo item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(item.LaunchTarget))
        {
            throw new InvalidOperationException("The dock item has no launch target.");
        }

        return Task.Run(() => LaunchCore(item), cancellationToken);
    }

    public Task ActivateWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (windowHandle == IntPtr.Zero || !IsWindow(windowHandle))
        {
            throw new InvalidOperationException("The application window is no longer available.");
        }

        return Task.Run(() =>
        {
            _ = ShowWindow(windowHandle, SwRestore);

            if (!SetForegroundWindow(windowHandle))
            {
                SwitchToThisWindow(windowHandle, true);
            }
        }, cancellationToken);
    }

    private static void LaunchCore(DockItemInfo item)
    {
        var target = item.LaunchTarget!;
        var arguments = item.LaunchArguments ?? string.Empty;

        // AUMID activation through the shell keeps packaged apps and shortcuts
        // on the supported Windows path instead of guessing package binaries.
        if (item.TargetKind == Domain.Dock.PinnedTargetKind.PackagedApp)
        {
            target = "explorer.exe";
            arguments = $"shell:AppsFolder\\{item.LaunchTarget}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = target,
            Arguments = arguments,
            UseShellExecute = true
        };

        using var process = Process.Start(startInfo);
    }

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);
}

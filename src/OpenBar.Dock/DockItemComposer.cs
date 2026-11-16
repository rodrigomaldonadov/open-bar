using OpenBar.Application.Dock;
using OpenBar.Domain.Dock;

namespace OpenBar.Dock;

/// <summary>
/// Merges persisted pinned applications with visible windows. Matching prefers
/// AppUserModelId, then the resolved executable path, then a direct executable
/// target. Windows already represented by a pinned tile become its running
/// indicator; the remaining window groups appear as temporary running tiles.
/// </summary>
public static class DockItemComposer
{
    public static IReadOnlyList<DockItemInfo> Compose(
        IReadOnlyList<PinnedApplication> pinnedApplications,
        IReadOnlyList<AppWindowSnapshot> windows)
    {
        ArgumentNullException.ThrowIfNull(pinnedApplications);
        ArgumentNullException.ThrowIfNull(windows);

        var matchedHandles = new HashSet<IntPtr>();
        var items = new List<DockItemInfo>(pinnedApplications.Count + windows.Count);

        foreach (var application in pinnedApplications.OrderBy(app => app.SortOrder).ThenBy(app => app.CreatedAt))
        {
            var matching = windows
                .Where(window => Matches(application, window))
                .ToArray();

            foreach (var window in matching)
            {
                _ = matchedHandles.Add(window.Handle);
            }

            items.Add(new DockItemInfo(
                application.Id,
                application.Id.ToString("N"),
                application.Name,
                application.Target,
                application.Arguments,
                application.TargetKind,
                application.IconKey ?? application.Target,
                IsPinned: true,
                IsRunning: matching.Length > 0,
                WindowHandle: matching.Length > 0 ? matching[0].Handle : null,
                application.SortOrder));
        }

        var groups = windows
            .Where(window => !matchedHandles.Contains(window.Handle))
            .GroupBy(GetWindowKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(window => window.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        for (var index = 0; index < groups.Length; index++)
        {
            var window = groups[index];
            items.Add(new DockItemInfo(
                null,
                "window:" + GetWindowKey(window),
                string.IsNullOrWhiteSpace(window.DisplayName) ? window.Title : window.DisplayName,
                LaunchTarget: null,
                LaunchArguments: null,
                TargetKind: PinnedTargetKind.Executable,
                IconPath: window.ProcessPath,
                IsPinned: false,
                IsRunning: true,
                WindowHandle: window.Handle,
                SortOrder: 10_000 + index));
        }

        return items;
    }

    private static bool Matches(PinnedApplication application, AppWindowSnapshot window)
    {
        if (!string.IsNullOrWhiteSpace(application.AppUserModelId)
            && !string.IsNullOrWhiteSpace(window.AppUserModelId)
            && application.AppUserModelId.Equals(window.AppUserModelId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(window.ProcessPath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(application.ResolvedTarget))
        {
            if (IsShellHost(application.ResolvedTarget))
            {
                // explorer.exe is the host for many unrelated shortcuts; only
                // an AUMID match is meaningful for those.
                return false;
            }

            if (PathsEqual(application.ResolvedTarget, window.ProcessPath))
            {
                return true;
            }
        }

        if (application.TargetKind == PinnedTargetKind.Executable)
        {
            if (PathsEqual(application.Target, window.ProcessPath))
            {
                return true;
            }

            // Windows app execution aliases (Notepad, Paint, Terminal, ...)
            // resolve to a packaged process in another directory. The file name
            // is the stable bridge in that case.
            return string.Equals(
                Path.GetFileName(application.Target),
                Path.GetFileName(window.ProcessPath),
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsShellHost(string path)
        => Path.GetFileName(path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
        catch (NotSupportedException)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string GetWindowKey(AppWindowSnapshot window)
    {
        if (!string.IsNullOrWhiteSpace(window.AppUserModelId))
        {
            return "aumid:" + window.AppUserModelId;
        }

        if (!string.IsNullOrWhiteSpace(window.ProcessPath))
        {
            return "path:" + window.ProcessPath;
        }

        return "pid:" + window.ProcessId;
    }
}

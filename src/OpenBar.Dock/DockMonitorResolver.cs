namespace OpenBar.Dock;

/// <summary>Logical monitor description used to resolve persisted layouts.</summary>
public sealed record DockMonitorInfo(string Id, bool IsPrimary, DockRect WorkArea);

/// <summary>
/// Resolves a persisted logical monitor selector. Unknown indices and removed
/// monitors fall back to the primary display instead of producing invalid
/// coordinates.
/// </summary>
public static class DockMonitorResolver
{
    public static DockMonitorInfo? Select(string? monitorTarget, IReadOnlyList<DockMonitorInfo> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            return null;
        }

        var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];

        if (string.IsNullOrWhiteSpace(monitorTarget)
            || monitorTarget.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            return primary;
        }

        const string prefix = "index:";
        if (monitorTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(monitorTarget.AsSpan(prefix.Length), out var index)
            && index >= 0
            && index < monitors.Count)
        {
            return monitors[index];
        }

        return primary;
    }
}

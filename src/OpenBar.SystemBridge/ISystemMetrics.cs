namespace OpenBar.SystemBridge;

/// <summary>Cumulative CPU times reported by <c>GetSystemTimes</c>.</summary>
public readonly record struct CpuTimesSample(ulong Idle, ulong Kernel, ulong User);

/// <summary>Physical memory values in bytes.</summary>
public readonly record struct MemorySample(ulong TotalBytes, ulong AvailableBytes);

/// <summary>Native source used by the shared metrics service; replaceable in tests.</summary>
public interface ISystemMetricsSource
{
    bool TryReadCpuTimes(out CpuTimesSample sample);

    bool TryReadMemory(out MemorySample sample);
}

/// <summary>Shared system measurements consumed by widgets and the dock status chip.</summary>
public sealed record SystemMetricsSnapshot(
    double? CpuUsagePercent,
    ulong? TotalMemoryBytes,
    ulong? AvailableMemoryBytes,
    ulong? UsedMemoryBytes,
    double? MemoryUsagePercent,
    bool CpuAvailable,
    bool MemoryAvailable,
    DateTimeOffset CapturedAtUtc)
{
    public static SystemMetricsSnapshot Empty { get; } = new(
        null,
        null,
        null,
        null,
        null,
        CpuAvailable: false,
        MemoryAvailable: false,
        CapturedAtUtc: DateTimeOffset.MinValue);
}

/// <summary>
/// Shared, demand-driven system metrics. Widgets call <see cref="RefreshAsync"/>
/// from their runtime scheduler; one sample is shared by all callers. Optional
/// subscriptions start one shared loop only while at least one consumer exists.
/// </summary>
public interface ISystemMetrics
{
    SystemMetricsSnapshot Current { get; }

    Task<SystemMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives shared updates. The shared polling loop is active only while at
    /// least one subscription exists. Disposing the returned handle stops it
    /// when the last subscriber leaves.
    /// </summary>
    IDisposable Subscribe(Action<SystemMetricsSnapshot> observer, TimeSpan? interval = null);
}

/// <summary>Neutral implementation used when the host does not register the bridge.</summary>
public sealed class NullSystemMetrics : ISystemMetrics
{
    public static NullSystemMetrics Instance { get; } = new();

    public SystemMetricsSnapshot Current => SystemMetricsSnapshot.Empty;

    public Task<SystemMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Current);

    public IDisposable Subscribe(Action<SystemMetricsSnapshot> observer, TimeSpan? interval = null)
        => EmptyDisposable.Instance;

    private sealed class EmptyDisposable : IDisposable
    {
        public static EmptyDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

/// <summary>Bounds for shared polling and cache reuse.</summary>
public sealed class SystemMetricsOptions
{
    public TimeSpan MinimumInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public TimeSpan DefaultInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaximumInterval { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMilliseconds(350);

    internal void Validate()
    {
        if (MinimumInterval <= TimeSpan.Zero
            || DefaultInterval < MinimumInterval
            || MaximumInterval < DefaultInterval
            || CacheDuration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SystemMetricsOptions),
                "System metrics intervals must satisfy 0 < minimum <= default <= maximum and cache >= 0.");
        }
    }

    internal TimeSpan Clamp(TimeSpan interval)
    {
        if (interval < MinimumInterval)
        {
            return MinimumInterval;
        }

        return interval > MaximumInterval ? MaximumInterval : interval;
    }
}

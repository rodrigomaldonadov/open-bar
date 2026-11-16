namespace OpenBar.Widgets.System;

public sealed record CpuWidgetSettings
{
    public int IntervalSeconds { get; init; } = 1;
}

public sealed record MemoryWidgetSettings
{
    public int IntervalSeconds { get; init; } = 2;

    /// <summary>auto, mb or gb.</summary>
    public string Unit { get; init; } = "auto";
}

public sealed record BatteryWidgetSettings
{
    public int IntervalSeconds { get; init; } = 5;
}

public sealed record NetworkWidgetSettings
{
    public int IntervalSeconds { get; init; } = 2;

    public string AdapterId { get; init; } = string.Empty;
}

public sealed record StorageWidgetSettings
{
    public int IntervalSeconds { get; init; } = 5;

    public string VolumeId { get; init; } = string.Empty;
}

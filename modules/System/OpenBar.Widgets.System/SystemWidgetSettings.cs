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

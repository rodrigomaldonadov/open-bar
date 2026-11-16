namespace OpenBar.Widgets.Clock;

/// <summary>Minimal configuration for the clock widget.</summary>
public sealed record ClockWidgetSettings
{
    public bool Use24Hour { get; init; } = true;

    public bool ShowSeconds { get; init; } = true;

    public bool ShowDate { get; init; } = true;
}

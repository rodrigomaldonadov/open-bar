namespace OpenBar.WidgetRuntime;

/// <summary>
/// Scheduler limits enforced by the runtime. Widgets cannot choose an
/// arbitrarily small interval or register an unbounded number of loops.
/// </summary>
public sealed class WidgetSchedulerOptions
{
    public TimeSpan MinimumInterval { get; set; } = TimeSpan.FromMilliseconds(100);

    public int MaxSchedulesPerInstance { get; set; } = 16;

    internal void Validate()
    {
        if (MinimumInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumInterval), "The minimum interval must be positive.");
        }

        if (MaxSchedulesPerInstance < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxSchedulesPerInstance),
                "At least one schedule per widget instance is required.");
        }
    }
}

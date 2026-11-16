namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Periodic work scheduler owned by the runtime. Widgets must schedule work
/// through this interface instead of creating their own timers, so the runtime
/// can enforce a minimum interval, prevent overlapping executions, isolate
/// exceptions and release every loop on stop.
/// </summary>
public interface IWidgetScheduler
{
    /// <summary>
    /// Runs <paramref name="callback"/> every <paramref name="interval"/>.
    /// The callback is never invoked concurrently with itself. Disposing the
    /// returned handle cancels only this schedule.
    /// </summary>
    IDisposable Schedule(string name, TimeSpan interval, Func<CancellationToken, Task> callback);
}

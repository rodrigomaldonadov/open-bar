using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime.Tests.Fakes;

internal sealed class RecordingScheduler : IWidgetScheduler
{
    private readonly List<RecordingSchedule> _schedules = [];

    public IReadOnlyList<RecordingSchedule> Schedules
    {
        get
        {
            lock (_schedules)
            {
                return _schedules.ToArray();
            }
        }
    }

    public IDisposable Schedule(string name, TimeSpan interval, Func<CancellationToken, Task> callback)
    {
        var schedule = new RecordingSchedule(name, interval, callback);
        lock (_schedules)
        {
            _schedules.Add(schedule);
        }

        return schedule;
    }

    internal sealed class RecordingSchedule : IDisposable
    {
        public RecordingSchedule(string name, TimeSpan interval, Func<CancellationToken, Task> callback)
        {
            Name = name;
            Interval = interval;
            Callback = callback;
        }

        public string Name { get; }

        public TimeSpan Interval { get; }

        public Func<CancellationToken, Task> Callback { get; }

        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}

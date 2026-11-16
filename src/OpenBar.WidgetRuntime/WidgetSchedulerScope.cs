using Microsoft.Extensions.Logging;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Schedules belonging to one widget instance. Each schedule is a single async
/// loop, so callbacks never overlap; a callback failure is logged and the loop
/// keeps running. Disposing the scope cancels and awaits every loop.
/// </summary>
public sealed partial class WidgetSchedulerScope : IWidgetScheduler, IAsyncDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<long, ScheduleEntry> _entries = new();
    private readonly CancellationTokenSource _scopeCts = new();
    private readonly WidgetSchedulerOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WidgetScheduler> _logger;
    private readonly Action<WidgetSchedulerScope> _onDisposed;
    private long _nextId;
    private bool _disposed;

    internal WidgetSchedulerScope(
        Guid instanceId,
        WidgetSchedulerOptions options,
        TimeProvider timeProvider,
        ILogger<WidgetScheduler> logger,
        Action<WidgetSchedulerScope> onDisposed)
    {
        InstanceId = instanceId;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _onDisposed = onDisposed;
    }

    public Guid InstanceId { get; }

    public IDisposable Schedule(string name, TimeSpan interval, Func<CancellationToken, Task> callback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A schedule name is required.", nameof(name));
        }

        ArgumentNullException.ThrowIfNull(callback);

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "The interval must be positive.");
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_entries.Count >= _options.MaxSchedulesPerInstance)
            {
                throw new WidgetSchedulerException(
                    $"Widget instance {InstanceId} reached the limit of {_options.MaxSchedulesPerInstance} schedules.");
            }

            var effectiveInterval = interval < _options.MinimumInterval ? _options.MinimumInterval : interval;
            if (effectiveInterval != interval)
            {
                LogIntervalClamped(_logger, InstanceId, name, interval, effectiveInterval);
            }

            var id = ++_nextId;
            var entry = new ScheduleEntry(
                name,
                callback,
                CancellationTokenSource.CreateLinkedTokenSource(_scopeCts.Token));

            _entries.Add(id, entry);
            entry.Task = RunLoopAsync(entry, effectiveInterval);
            return new WidgetScheduleHandle(this, id, entry);
        }
    }

    public async ValueTask DisposeAsync()
    {
        ScheduleEntry[] entries;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            entries = _entries.Values.ToArray();
            _entries.Clear();
        }

        foreach (var entry in entries)
        {
            entry.Cancel();
        }

        foreach (var entry in entries)
        {
            await entry.Task.ConfigureAwait(false);
        }

        _scopeCts.Dispose();
        _onDisposed(this);
    }

    private void ReleaseEntry(long id, ScheduleEntry entry)
    {
        lock (_sync)
        {
            _entries.Remove(id);
        }

        // Cancelling is enough: the loop observes the token, exits and disposes
        // its own cancellation source in its finally block. Disposing the source
        // here could race with a pending PeriodicTimer wait.
        entry.Cancel();
    }

    private async Task RunLoopAsync(ScheduleEntry entry, TimeSpan interval)
    {
        using var timer = new PeriodicTimer(interval, _timeProvider);
        var token = entry.Token;

        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await entry.Callback(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogTickFailed(_logger, InstanceId, entry.Name, ex);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Normal shutdown path.
        }
        catch (Exception ex)
        {
            LogLoopFailed(_logger, InstanceId, entry.Name, ex);
        }
        finally
        {
            entry.DisposeCancellation();
        }
    }

    private sealed class ScheduleEntry
    {
        private int _canceled;
        private int _cancellationDisposed;

        public ScheduleEntry(string name, Func<CancellationToken, Task> callback, CancellationTokenSource cancellation)
        {
            Name = name;
            Callback = callback;
            Cancellation = cancellation;
        }

        public string Name { get; }

        public Func<CancellationToken, Task> Callback { get; }

        public CancellationTokenSource Cancellation { get; }

        public CancellationToken Token => Cancellation.Token;

        public Task Task { get; set; } = Task.CompletedTask;

        public void Cancel()
        {
            if (Interlocked.Exchange(ref _canceled, 1) != 0)
            {
                return;
            }

            try
            {
                Cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The scope already released the cancellation source.
            }
        }

        public void DisposeCancellation()
        {
            if (Interlocked.Exchange(ref _cancellationDisposed, 1) == 0)
            {
                Cancellation.Dispose();
            }
        }
    }

    private sealed class WidgetScheduleHandle : IDisposable
    {
        private readonly WidgetSchedulerScope _owner;
        private readonly long _id;
        private readonly ScheduleEntry _entry;
        private int _disposed;

        public WidgetScheduleHandle(WidgetSchedulerScope owner, long id, ScheduleEntry entry)
        {
            _owner = owner;
            _id = id;
            _entry = entry;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.ReleaseEntry(_id, _entry);
            }
        }
    }

    [LoggerMessage(EventId = 5101, Level = LogLevel.Debug, Message = "Schedule {ScheduleName} for widget instance {InstanceId} was clamped from {Requested} to {Effective}.")]
    private static partial void LogIntervalClamped(
        ILogger<WidgetScheduler> logger,
        Guid instanceId,
        string scheduleName,
        TimeSpan requested,
        TimeSpan effective);

    [LoggerMessage(EventId = 5102, Level = LogLevel.Error, Message = "Schedule {ScheduleName} for widget instance {InstanceId} threw; the loop will continue.")]
    private static partial void LogTickFailed(
        ILogger<WidgetScheduler> logger,
        Guid instanceId,
        string scheduleName,
        Exception exception);

    [LoggerMessage(EventId = 5103, Level = LogLevel.Error, Message = "Schedule {ScheduleName} for widget instance {InstanceId} stopped unexpectedly.")]
    private static partial void LogLoopFailed(
        ILogger<WidgetScheduler> logger,
        Guid instanceId,
        string scheduleName,
        Exception exception);
}

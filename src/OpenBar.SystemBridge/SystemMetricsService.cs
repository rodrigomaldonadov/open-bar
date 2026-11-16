using Microsoft.Extensions.Logging;

namespace OpenBar.SystemBridge;

/// <summary>
/// Samples system metrics once per cache window and shares the result between
/// all callers. CPU needs two cumulative samples; the first refresh therefore
/// reports an unavailable CPU value instead of inventing a percentage.
/// </summary>
public sealed partial class SystemMetricsService : ISystemMetrics, IAsyncDisposable
{
    private readonly ISystemMetricsSource _source;
    private readonly SystemMetricsOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SystemMetricsService> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _subscriberLock = new();
    private readonly Dictionary<long, Subscriber> _subscribers = new();
    private readonly CancellationTokenSource _disposeCts = new();

    private SystemMetricsSnapshot _current = SystemMetricsSnapshot.Empty;
    private CpuTimesSample? _previousCpu;
    private DateTimeOffset _lastCaptured = DateTimeOffset.MinValue;
    private long _nextSubscriberId;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private TimeSpan? _loopInterval;
    private int _disposed;

    public SystemMetricsService(
        ISystemMetricsSource source,
        SystemMetricsOptions options,
        TimeProvider timeProvider,
        ILogger<SystemMetricsService> logger)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        options.Validate();
        _source = source;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public SystemMetricsSnapshot Current => Volatile.Read(ref _current);

    public async Task<SystemMetricsSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var now = _timeProvider.GetUtcNow();
        if (now - _lastCaptured < _options.CacheDuration)
        {
            return Current;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            now = _timeProvider.GetUtcNow();
            if (now - _lastCaptured < _options.CacheDuration)
            {
                return Current;
            }

            var snapshot = Capture(now);
            _lastCaptured = now;
            Volatile.Write(ref _current, snapshot);
            Publish(snapshot);
            return snapshot;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public IDisposable Subscribe(Action<SystemMetricsSnapshot> observer, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(observer);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var effectiveInterval = _options.Clamp(interval ?? _options.DefaultInterval);
        var id = Interlocked.Increment(ref _nextSubscriberId);

        lock (_subscriberLock)
        {
            _subscribers[id] = new Subscriber(observer, effectiveInterval);
            EnsureLoopLocked();
        }

        return new Subscription(this, id);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _disposeCts.CancelAsync().ConfigureAwait(false);

        Task? loopTask;
        CancellationTokenSource? loopCts;
        lock (_subscriberLock)
        {
            loopTask = _loopTask;
            loopCts = _loopCts;
            _loopTask = null;
            _loopCts = null;
            _subscribers.Clear();
        }

        loopCts?.Cancel();

        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        loopCts?.Dispose();
        _disposeCts.Dispose();
        _refreshGate.Dispose();
    }

    private SystemMetricsSnapshot Capture(DateTimeOffset now)
    {
        double? cpu = null;
        var cpuAvailable = false;

        try
        {
            if (_source.TryReadCpuTimes(out var times))
            {
                if (_previousCpu is { } previous)
                {
                    var idleDelta = times.Idle >= previous.Idle ? times.Idle - previous.Idle : 0;
                    var kernelDelta = times.Kernel >= previous.Kernel ? times.Kernel - previous.Kernel : 0;
                    var userDelta = times.User >= previous.User ? times.User - previous.User : 0;
                    var totalDelta = kernelDelta + userDelta;

                    if (totalDelta > 0)
                    {
                        cpu = Math.Clamp(
                            (1.0 - (double)idleDelta / totalDelta) * 100.0,
                            0,
                            100);
                        cpuAvailable = true;
                    }
                }

                _previousCpu = times;
            }
            else
            {
                _previousCpu = null;
            }
        }
        catch (Exception ex)
        {
            _previousCpu = null;
            LogCpuReadFailed(_logger, ex);
        }

        ulong? total = null;
        ulong? available = null;
        ulong? used = null;
        double? memoryPercent = null;
        var memoryAvailable = false;

        try
        {
            if (_source.TryReadMemory(out var memory) && memory.TotalBytes > 0)
            {
                var availableBytes = Math.Min(memory.AvailableBytes, memory.TotalBytes);
                total = memory.TotalBytes;
                available = availableBytes;
                used = memory.TotalBytes - availableBytes;
                memoryPercent = Math.Clamp((double)used.Value / memory.TotalBytes * 100.0, 0, 100);
                memoryAvailable = true;
            }
        }
        catch (Exception ex)
        {
            LogMemoryReadFailed(_logger, ex);
        }

        return new SystemMetricsSnapshot(
            cpu,
            total,
            available,
            used,
            memoryPercent,
            cpuAvailable,
            memoryAvailable,
            now);
    }

    private void Publish(SystemMetricsSnapshot snapshot)
    {
        Subscriber[] subscribers;
        lock (_subscriberLock)
        {
            subscribers = _subscribers.Values.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                subscriber.Observer(snapshot);
            }
            catch (Exception ex)
            {
                LogSubscriberFailed(_logger, ex);
            }
        }
    }

    private void EnsureLoopLocked()
    {
        if (_subscribers.Count == 0)
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
            _loopCts = null;
            _loopTask = null;
            _loopInterval = null;
            return;
        }

        var desired = _subscribers.Values.Min(subscriber => subscriber.Interval);
        if (_loopCts is not null && _loopInterval == desired)
        {
            return;
        }

        _loopCts?.Cancel();
        _loopCts?.Dispose();

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
        _loopCts = cts;
        _loopInterval = desired;
        _loopTask = Task.Run(() => RunLoopAsync(desired, cts.Token), CancellationToken.None);
    }

    private async Task RunLoopAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(interval, _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    _ = await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    LogLoopFailed(_logger, ex);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogLoopFailed(_logger, ex);
        }
    }

    private void RemoveSubscriber(long id)
    {
        lock (_subscriberLock)
        {
            if (_subscribers.Remove(id))
            {
                EnsureLoopLocked();
            }
        }
    }

    private sealed record Subscriber(Action<SystemMetricsSnapshot> Observer, TimeSpan Interval);

    private sealed class Subscription : IDisposable
    {
        private readonly SystemMetricsService _owner;
        private readonly long _id;
        private int _disposed;

        public Subscription(SystemMetricsService owner, long id)
        {
            _owner = owner;
            _id = id;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.RemoveSubscriber(_id);
            }
        }
    }

    [LoggerMessage(EventId = 9101, Level = LogLevel.Warning, Message = "CPU metrics could not be read; keeping the previous snapshot unavailable.")]
    private static partial void LogCpuReadFailed(ILogger<SystemMetricsService> logger, Exception exception);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Warning, Message = "Memory metrics could not be read.")]
    private static partial void LogMemoryReadFailed(ILogger<SystemMetricsService> logger, Exception exception);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Warning, Message = "A system metrics subscriber threw; other subscribers continue.")]
    private static partial void LogSubscriberFailed(ILogger<SystemMetricsService> logger, Exception exception);

    [LoggerMessage(EventId = 9104, Level = LogLevel.Warning, Message = "The shared system metrics loop failed.")]
    private static partial void LogLoopFailed(ILogger<SystemMetricsService> logger, Exception exception);
}

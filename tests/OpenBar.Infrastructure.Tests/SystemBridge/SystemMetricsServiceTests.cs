using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.SystemBridge;
using Xunit;

namespace OpenBar.Infrastructure.Tests.SystemBridge;

public sealed class SystemMetricsServiceTests
{
    private static readonly SystemMetricsOptions ImmediateOptions = new()
    {
        MinimumInterval = TimeSpan.FromMilliseconds(10),
        DefaultInterval = TimeSpan.FromMilliseconds(20),
        MaximumInterval = TimeSpan.FromMilliseconds(100),
        CacheDuration = TimeSpan.Zero
    };

    [Fact]
    public async Task FirstCpuSampleIsUnavailableButMemoryIsImmediate()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Memory.Enqueue(new MemorySample(1_000, 400));

        await using var service = Create(source);

        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.CpuAvailable);
        Assert.Null(snapshot.CpuUsagePercent);
        Assert.True(snapshot.MemoryAvailable);
        Assert.Equal(1_000ul, snapshot.TotalMemoryBytes);
        Assert.Equal(400ul, snapshot.AvailableMemoryBytes);
        Assert.Equal(600ul, snapshot.UsedMemoryBytes);
        Assert.Equal(60, snapshot.MemoryUsagePercent);
    }

    [Fact]
    public async Task SecondCpuSampleUsesTheDeltaAndClampsTo100()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Cpu.Enqueue(new CpuTimesSample(110, 230, 130));
        source.Memory.Enqueue(new MemorySample(1_000, 500));
        source.Memory.Enqueue(new MemorySample(1_000, 500));

        await using var service = Create(source);
        _ = await service.RefreshAsync();
        var second = await service.RefreshAsync();

        Assert.True(second.CpuAvailable);
        Assert.Equal(83.333, second.CpuUsagePercent!.Value, 2);
    }

    [Fact]
    public async Task NativeFailuresProduceControlledUnavailableValues()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(null);
        source.Memory.Enqueue(null);

        await using var service = Create(source);
        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.CpuAvailable);
        Assert.False(snapshot.MemoryAvailable);
        Assert.Null(snapshot.TotalMemoryBytes);
        Assert.Null(snapshot.MemoryUsagePercent);
    }

    [Fact]
    public async Task ConcurrentRefreshesShareOneNativeSample()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Cpu.Enqueue(new CpuTimesSample(110, 230, 130));
        source.Memory.Enqueue(new MemorySample(1_000, 500));
        source.Memory.Enqueue(new MemorySample(1_000, 500));

        var options = new SystemMetricsOptions
        {
            MinimumInterval = TimeSpan.FromMilliseconds(10),
            DefaultInterval = TimeSpan.FromMilliseconds(20),
            MaximumInterval = TimeSpan.FromMilliseconds(100),
            CacheDuration = TimeSpan.FromSeconds(1)
        };

        await using var service = Create(source, options);
        var results = await Task.WhenAll(
            service.RefreshAsync(),
            service.RefreshAsync(),
            service.RefreshAsync());

        Assert.All(results, snapshot => Assert.Equal(results[0], snapshot));
        Assert.Equal(1, source.CpuReads);
        Assert.Equal(1, source.MemoryReads);
    }

    [Fact]
    public async Task SubscriptionPublishesSharedSamplesAndStopsWhenDisposed()
    {
        var source = new FakeSource();
        for (var index = 0; index < 6; index++)
        {
            source.Cpu.Enqueue(new CpuTimesSample((ulong)(100 + index), 200, 100));
            source.Memory.Enqueue(new MemorySample(1_000, 500));
        }

        await using var service = Create(source);
        var notifications = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using (service.Subscribe(
                   _ =>
                   {
                       if (Interlocked.Increment(ref notifications) >= 2)
                       {
                           reached.TrySetResult();
                       }
                   },
                   TimeSpan.FromMilliseconds(20)))
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var afterDispose = Volatile.Read(ref notifications);
        await Task.Delay(100);

        Assert.Equal(afterDispose, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task SubscriberExceptionsDoNotStopOtherSubscribers()
    {
        var source = new FakeSource();
        for (var index = 0; index < 8; index++)
        {
            source.Cpu.Enqueue(new CpuTimesSample((ulong)(100 + index), 200, 100));
            source.Memory.Enqueue(new MemorySample(1_000, 500));
        }

        await using var service = Create(source);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var bad = service.Subscribe(
            _ => throw new InvalidOperationException("subscriber failure"),
            TimeSpan.FromMilliseconds(20));
        using var good = service.Subscribe(
            _ => reached.TrySetResult(),
            TimeSpan.FromMilliseconds(20));

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CanceledRefreshHonorsCancellation()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Memory.Enqueue(new MemorySample(1_000, 500));

        await using var service = Create(source);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.RefreshAsync(cts.Token));
    }

    [Fact]
    public async Task DisposeStopsTheSharedLoop()
    {
        var source = new FakeSource();
        for (var index = 0; index < 6; index++)
        {
            source.Cpu.Enqueue(new CpuTimesSample((ulong)(100 + index), 200, 100));
            source.Memory.Enqueue(new MemorySample(1_000, 500));
        }

        var service = Create(source);
        var notifications = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = service.Subscribe(
            _ =>
            {
                Interlocked.Increment(ref notifications);
                reached.TrySetResult();
            },
            TimeSpan.FromMilliseconds(20));

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.DisposeAsync();

        var afterDispose = Volatile.Read(ref notifications);
        await Task.Delay(100);
        Assert.Equal(afterDispose, Volatile.Read(ref notifications));
    }

    [Fact]
    public async Task CpuUsageClampsToThe0To100Range()
    {
        var highSource = new FakeSource();
        highSource.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        highSource.Cpu.Enqueue(new CpuTimesSample(100, 300, 200));
        highSource.Memory.Enqueue(new MemorySample(1_000, 500));
        highSource.Memory.Enqueue(new MemorySample(1_000, 500));

        await using (var high = Create(highSource))
        {
            _ = await high.RefreshAsync();
            var snapshot = await high.RefreshAsync();
            Assert.True(snapshot.CpuAvailable);
            Assert.Equal(100, snapshot.CpuUsagePercent);
        }

        var lowSource = new FakeSource();
        lowSource.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        lowSource.Cpu.Enqueue(new CpuTimesSample(500, 250, 150));
        lowSource.Memory.Enqueue(new MemorySample(1_000, 500));
        lowSource.Memory.Enqueue(new MemorySample(1_000, 500));

        await using (var low = Create(lowSource))
        {
            _ = await low.RefreshAsync();
            var snapshot = await low.RefreshAsync();
            Assert.True(snapshot.CpuAvailable);
            Assert.Equal(0, snapshot.CpuUsagePercent);
        }
    }

    [Fact]
    public async Task ZeroCpuDeltaProducesAnUnavailableValue()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Memory.Enqueue(new MemorySample(1_000, 500));
        source.Memory.Enqueue(new MemorySample(1_000, 500));

        await using var service = Create(source);
        _ = await service.RefreshAsync();
        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.CpuAvailable);
        Assert.Null(snapshot.CpuUsagePercent);
    }

    [Fact]
    public async Task MemoryAvailableIsClampedToTotal()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Memory.Enqueue(new MemorySample(1_000, 2_000));

        await using var service = Create(source);
        var snapshot = await service.RefreshAsync();

        Assert.True(snapshot.MemoryAvailable);
        Assert.Equal(1_000ul, snapshot.AvailableMemoryBytes);
        Assert.Equal(0ul, snapshot.UsedMemoryBytes);
        Assert.Equal(0, snapshot.MemoryUsagePercent);
    }

    [Fact]
    public async Task ZeroTotalMemoryIsUnavailable()
    {
        var source = new FakeSource();
        source.Cpu.Enqueue(new CpuTimesSample(100, 200, 100));
        source.Memory.Enqueue(new MemorySample(0, 0));

        await using var service = Create(source);
        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.MemoryAvailable);
        Assert.Null(snapshot.TotalMemoryBytes);
        Assert.Null(snapshot.MemoryUsagePercent);
    }

    private static SystemMetricsService Create(
        ISystemMetricsSource source,
        SystemMetricsOptions? options = null)
        => new(
            source,
            options ?? ImmediateOptions,
            TimeProvider.System,
            NullLogger<SystemMetricsService>.Instance);

    private sealed class FakeSource : ISystemMetricsSource
    {
        public Queue<CpuTimesSample?> Cpu { get; } = new();

        public Queue<MemorySample?> Memory { get; } = new();

        public int CpuReads { get; private set; }

        public int MemoryReads { get; private set; }

        public bool TryReadCpuTimes(out CpuTimesSample sample)
        {
            CpuReads++;
            var value = Cpu.Count > 0 ? Cpu.Dequeue() : null;

            if (value is { } cpu)
            {
                sample = cpu;
                return true;
            }

            sample = default;
            return false;
        }

        public bool TryReadMemory(out MemorySample sample)
        {
            MemoryReads++;
            var value = Memory.Count > 0 ? Memory.Dequeue() : null;

            if (value is { } memory)
            {
                sample = memory;
                return true;
            }

            sample = default;
            return false;
        }
    }
}

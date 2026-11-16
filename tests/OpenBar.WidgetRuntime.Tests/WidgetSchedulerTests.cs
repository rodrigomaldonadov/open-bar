using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.WidgetRuntime;
using Xunit;

namespace OpenBar.WidgetRuntime.Tests;

public sealed class WidgetSchedulerTests
{
    [Fact]
    public async Task RunsPeriodicallyUntilDisposed()
    {
        await using var scheduler = CreateScheduler();
        await using var scope = scheduler.CreateScope(Guid.NewGuid());

        var count = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = scope.Schedule(
            "tick",
            TimeSpan.FromMilliseconds(15),
            _ =>
            {
                if (Interlocked.Increment(ref count) >= 3)
                {
                    reached.TrySetResult();
                }

                return Task.CompletedTask;
            });

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Volatile.Read(ref count) >= 3);
    }

    [Fact]
    public async Task DisposingTheScopeStopsEveryLoop()
    {
        var scheduler = CreateScheduler();
        var scope = scheduler.CreateScope(Guid.NewGuid());
        var count = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = scope.Schedule(
            "tick",
            TimeSpan.FromMilliseconds(10),
            _ =>
            {
                if (Interlocked.Increment(ref count) >= 2)
                {
                    reached.TrySetResult();
                }

                return Task.CompletedTask;
            });

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scope.DisposeAsync();

        var afterDispose = Volatile.Read(ref count);
        await Task.Delay(120);

        Assert.Equal(afterDispose, Volatile.Read(ref count));
        await scheduler.DisposeAsync();
    }

    [Fact]
    public async Task CallbacksNeverOverlap()
    {
        await using var scheduler = CreateScheduler();
        await using var scope = scheduler.CreateScope(Guid.NewGuid());

        var concurrent = 0;
        var maxConcurrent = 0;
        var ticks = 0;

        using var handle = scope.Schedule(
            "slow",
            TimeSpan.FromMilliseconds(10),
            async _ =>
            {
                var current = Interlocked.Increment(ref concurrent);
                Interlocked.Increment(ref ticks);

                int observed;
                while (current > (observed = Volatile.Read(ref maxConcurrent))
                       && Interlocked.CompareExchange(ref maxConcurrent, current, observed) != observed)
                {
                }

                await Task.Delay(60, CancellationToken.None).ConfigureAwait(false);
                Interlocked.Decrement(ref concurrent);
            });

        await Task.Delay(300);

        Assert.Equal(1, Volatile.Read(ref maxConcurrent));
        Assert.True(Volatile.Read(ref ticks) >= 2);
    }

    [Fact]
    public async Task CallbackExceptionsAreLoggedAndTheLoopContinues()
    {
        await using var scheduler = CreateScheduler();
        await using var scope = scheduler.CreateScope(Guid.NewGuid());

        var count = 0;
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = scope.Schedule(
            "flaky",
            TimeSpan.FromMilliseconds(10),
            _ =>
            {
                var current = Interlocked.Increment(ref count);

                if (current < 3)
                {
                    throw new InvalidOperationException("Tick failure.");
                }

                reached.TrySetResult();
                return Task.CompletedTask;
            });

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(Volatile.Read(ref count) >= 3);
    }

    [Fact]
    public async Task NonPositiveIntervalIsRejected()
    {
        await using var scheduler = CreateScheduler();
        await using var scope = scheduler.CreateScope(Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => scope.Schedule("bad", TimeSpan.Zero, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task ScheduleCountIsLimitedPerInstance()
    {
        await using var scheduler = new WidgetScheduler(
            new WidgetSchedulerOptions
            {
                MinimumInterval = TimeSpan.FromMilliseconds(10),
                MaxSchedulesPerInstance = 1
            },
            TimeProvider.System,
            NullLogger<WidgetScheduler>.Instance);

        await using var scope = scheduler.CreateScope(Guid.NewGuid());
        using var first = scope.Schedule("one", TimeSpan.FromSeconds(5), _ => Task.CompletedTask);

        Assert.Throws<WidgetSchedulerException>(
            () => scope.Schedule("two", TimeSpan.FromSeconds(5), _ => Task.CompletedTask));
    }

    [Fact]
    public async Task DisposingTheRootSchedulerStopsAllScopes()
    {
        var scheduler = CreateScheduler();
        var scope = scheduler.CreateScope(Guid.NewGuid());
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;

        _ = scope.Schedule(
            "tick",
            TimeSpan.FromMilliseconds(10),
            _ =>
            {
                Interlocked.Increment(ref count);
                reached.TrySetResult();
                return Task.CompletedTask;
            });

        await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scheduler.DisposeAsync();
        var afterDispose = Volatile.Read(ref count);
        await Task.Delay(100);

        Assert.Equal(afterDispose, Volatile.Read(ref count));
    }

    [Fact]
    public async Task CallbacksReceiveACancellationSignalWhenStopped()
    {
        await using var scheduler = CreateScheduler();
        await using var scope = scheduler.CreateScope(Guid.NewGuid());

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var handle = scope.Schedule(
            "long",
            TimeSpan.FromMilliseconds(10),
            async token =>
            {
                entered.TrySetResult();

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    canceled.TrySetResult();
                    throw;
                }
            });

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await scope.DisposeAsync();

        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static WidgetScheduler CreateScheduler()
        => new(
            new WidgetSchedulerOptions { MinimumInterval = TimeSpan.FromMilliseconds(10) },
            TimeProvider.System,
            NullLogger<WidgetScheduler>.Instance);
}

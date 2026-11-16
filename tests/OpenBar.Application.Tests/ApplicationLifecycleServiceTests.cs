using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Application.Lifecycle;
using Xunit;

namespace OpenBar.Application.Tests;

public sealed class ApplicationLifecycleServiceTests
{
    [Fact]
    public void NewServiceStartsInStartingState()
    {
        using var lifecycle = CreateService();

        Assert.Equal(ApplicationLifecycleState.Starting, lifecycle.State);
        Assert.False(lifecycle.IsShuttingDown);
    }

    [Fact]
    public void MarkRunningTransitionsToRunning()
    {
        using var lifecycle = CreateService();

        lifecycle.MarkRunning();

        Assert.Equal(ApplicationLifecycleState.Running, lifecycle.State);
        Assert.False(lifecycle.IsShuttingDown);
    }

    [Fact]
    public void MarkRunningIsIgnoredAfterShutdownWasRequested()
    {
        using var lifecycle = CreateService();

        lifecycle.RequestShutdown("Test");
        lifecycle.MarkRunning();

        Assert.Equal(ApplicationLifecycleState.ShuttingDown, lifecycle.State);
        Assert.True(lifecycle.IsShuttingDown);
    }

    [Fact]
    public void RequestShutdownRaisesEventExactlyOnce()
    {
        using var lifecycle = CreateService();
        var raised = 0;
        string? reason = null;
        lifecycle.ShutdownRequested += (_, e) =>
        {
            raised++;
            reason = e.Reason;
        };

        lifecycle.RequestShutdown("SystemTrayExit");
        lifecycle.RequestShutdown("SecondAttempt");

        Assert.Equal(1, raised);
        Assert.Equal("SystemTrayExit", reason);
        Assert.True(lifecycle.IsShuttingDown);
    }

    [Fact]
    public void RequestShutdownInvokesRemainingHandlersWhenOneThrows()
    {
        using var lifecycle = CreateService();
        var secondHandlerCalled = false;

        lifecycle.ShutdownRequested += (_, _) => throw new InvalidOperationException("boom");
        lifecycle.ShutdownRequested += (_, _) => secondHandlerCalled = true;

        lifecycle.RequestShutdown("Test");

        Assert.True(secondHandlerCalled);
    }

    [Fact]
    public void MarkStoppedTransitionsToStopped()
    {
        using var lifecycle = CreateService();
        lifecycle.MarkRunning();

        lifecycle.MarkStopped();

        Assert.Equal(ApplicationLifecycleState.Stopped, lifecycle.State);
        Assert.True(lifecycle.IsShuttingDown);
    }

    [Fact]
    public void DisposeClearsHandlersAndSurvivesFurtherCalls()
    {
        var lifecycle = CreateService();
        var raised = false;
        lifecycle.ShutdownRequested += (_, _) => raised = true;

        lifecycle.Dispose();
        lifecycle.Dispose();
        lifecycle.RequestShutdown("AfterDispose");
        lifecycle.MarkRunning();
        lifecycle.MarkStopped();

        Assert.False(raised);
        Assert.Equal(ApplicationLifecycleState.Starting, lifecycle.State);
    }

    private static ApplicationLifecycleService CreateService() =>
        new(NullLogger<ApplicationLifecycleService>.Instance);
}

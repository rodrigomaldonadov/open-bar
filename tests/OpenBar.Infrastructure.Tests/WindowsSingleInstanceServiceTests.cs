using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Infrastructure.Windows.SingleInstance;
using Xunit;

namespace OpenBar.Infrastructure.Tests;

public sealed class WindowsSingleInstanceServiceTests
{
    [Fact]
    public void TryAcquireIsIdempotentForTheSameService()
    {
        using var service = CreateService();

        Assert.True(service.TryAcquire());
        Assert.True(service.TryAcquire());
    }

    [Fact]
    public void SecondServiceCannotAcquireWhileFirstHoldsTheMutex()
    {
        var applicationId = NewApplicationId();
        using var first = CreateService(applicationId);
        using var second = CreateService(applicationId);

        Assert.True(first.TryAcquire());
        Assert.False(second.TryAcquire());
    }

    [Fact]
    public void MutexIsReleasedWhenTheOwningServiceIsDisposed()
    {
        var applicationId = NewApplicationId();

        var first = CreateService(applicationId);
        Assert.True(first.TryAcquire());
        first.Dispose();

        using var second = CreateService(applicationId);
        Assert.True(second.TryAcquire());
    }

    [Fact]
    public async Task ActivationRequestReachesThePrimaryInstance()
    {
        var applicationId = NewApplicationId();
        using var primary = CreateService(applicationId);
        using var secondary = CreateService(applicationId);

        Assert.True(primary.TryAcquire());
        Assert.False(secondary.TryAcquire());

        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        primary.ActivationRequested += (_, _) => activated.TrySetResult();

        await primary.StartAsync(CancellationToken.None);

        Assert.True(await secondary.NotifyExistingInstanceAsync(TimeSpan.FromSeconds(5)));
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await primary.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task NotifyFailsWhenNoListenerIsRunning()
    {
        using var service = CreateService();

        Assert.False(await service.NotifyExistingInstanceAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public async Task StartWithoutMutexDoesNotCreateAListener()
    {
        var applicationId = NewApplicationId();
        using var owner = CreateService(applicationId);
        using var other = CreateService(applicationId);

        Assert.True(owner.TryAcquire());
        Assert.False(other.TryAcquire());

        // Even though another instance exists, this service never acquired the
        // mutex, so it must not open a competing activation listener.
        await other.StartAsync(CancellationToken.None);

        Assert.False(await other.NotifyExistingInstanceAsync(TimeSpan.FromMilliseconds(500)));

        await other.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void DisposedServiceRejectsFurtherUse()
    {
        var service = CreateService();
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.TryAcquire());
        Assert.Throws<ObjectDisposedException>(() => service.StartAsync(CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task AsyncDisposedServiceRejectsFurtherUse()
    {
        var service = CreateService();

        Assert.True(service.TryAcquire());
        await service.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(() => service.TryAcquire());
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => service.NotifyExistingInstanceAsync(TimeSpan.FromMilliseconds(100)));
    }

    private static WindowsSingleInstanceService CreateService(string? applicationId = null) =>
        new(
            new SingleInstanceOptions { ApplicationId = applicationId ?? NewApplicationId() },
            NullLogger<WindowsSingleInstanceService>.Instance);

    private static string NewApplicationId() => $"OpenBarTest{Guid.NewGuid():N}";
}

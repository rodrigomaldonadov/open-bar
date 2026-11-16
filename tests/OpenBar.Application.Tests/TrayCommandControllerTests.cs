using Microsoft.Extensions.Logging.Abstractions;
using OpenBar.Application.Lifecycle;
using OpenBar.Application.Windows;
using Xunit;

namespace OpenBar.Application.Tests;

public sealed class TrayCommandControllerTests
{
    [Fact]
    public void OpenDockShowsAndActivatesTheWindow()
    {
        var windows = new FakeWindowManagementService();
        var controller = CreateController(windows);

        controller.OpenDock();

        Assert.Equal(1, windows.ShowAndActivateCalls);
        Assert.Equal(0, windows.ToggleCalls);
    }

    [Fact]
    public void ToggleDockDelegatesToWindowToggle()
    {
        var windows = new FakeWindowManagementService();
        var controller = CreateController(windows);

        controller.ToggleDock();

        Assert.Equal(1, windows.ToggleCalls);
        Assert.Equal(0, windows.ShowAndActivateCalls);
    }

    [Fact]
    public void ExitApplicationRequestsLifecycleShutdownWithTrayReason()
    {
        var lifecycle = new FakeApplicationLifecycle();
        var controller = CreateController(new FakeWindowManagementService(), lifecycle);

        controller.ExitApplication();

        Assert.Equal(1, lifecycle.RequestShutdownCalls);
        Assert.Equal("SystemTrayExit", lifecycle.LastReason);
        Assert.Equal(ApplicationLifecycleState.ShuttingDown, lifecycle.State);
    }

    private static TrayCommandController CreateController(
        IWindowManagementService windows,
        IApplicationLifecycle? lifecycle = null) =>
        new(
            windows,
            lifecycle ?? new FakeApplicationLifecycle(),
            NullLogger<TrayCommandController>.Instance);

    private sealed class FakeWindowManagementService : IWindowManagementService
    {
        public bool IsVisible { get; private set; }

        public int ShowCalls { get; private set; }

        public int HideCalls { get; private set; }

        public int ToggleCalls { get; private set; }

        public int ShowAndActivateCalls { get; private set; }

        public void Show()
        {
            ShowCalls++;
            IsVisible = true;
        }

        public void Hide()
        {
            HideCalls++;
            IsVisible = false;
        }

        public void ToggleVisibility()
        {
            ToggleCalls++;
            IsVisible = !IsVisible;
        }

        public void ShowAndActivate()
        {
            ShowAndActivateCalls++;
            IsVisible = true;
        }
    }

    private sealed class FakeApplicationLifecycle : IApplicationLifecycle
    {
        public ApplicationLifecycleState State { get; private set; } = ApplicationLifecycleState.Starting;

        public bool IsShuttingDown => State is ApplicationLifecycleState.ShuttingDown or ApplicationLifecycleState.Stopped;

        public event EventHandler<ApplicationShutdownRequestedEventArgs>? ShutdownRequested;

        public int RequestShutdownCalls { get; private set; }

        public string? LastReason { get; private set; }

        public void MarkRunning() => State = ApplicationLifecycleState.Running;

        public void RequestShutdown(string reason)
        {
            if (IsShuttingDown)
            {
                return;
            }

            State = ApplicationLifecycleState.ShuttingDown;
            RequestShutdownCalls++;
            LastReason = reason;
            ShutdownRequested?.Invoke(this, new ApplicationShutdownRequestedEventArgs(reason));
        }

        public void MarkStopped() => State = ApplicationLifecycleState.Stopped;

        public void Dispose()
        {
        }
    }
}

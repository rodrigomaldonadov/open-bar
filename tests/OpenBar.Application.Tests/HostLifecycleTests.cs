using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace OpenBar.Application.Tests;

public sealed class HostLifecycleTests
{
    [Fact]
    public async Task HostedServicesStartInRegistrationOrderAndStopInReverseOrder()
    {
        var log = new List<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IHostedService>(sp => new TrackingHostedService("A", sp.GetRequiredService<List<string>>()));
                services.AddSingleton<IHostedService>(sp => new TrackingHostedService("B", sp.GetRequiredService<List<string>>()));
                services.AddSingleton(log);
            })
            .Build();

        try
        {
            await host.StartAsync();
            await host.StopAsync();
        }
        finally
        {
            host.Dispose();
        }

        Assert.Equal(4, log.Count);
        Assert.Equal("A:start", log[0]);
        Assert.Equal("B:start", log[1]);
        Assert.Equal("B:stop", log[2]);
        Assert.Equal("A:stop", log[3]);
    }

    [Fact]
    public void DisposingTheHostDisposesContainerCreatedSingletons()
    {
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSingleton<DisposableProbe>())
            .Build();

        var probe = host.Services.GetRequiredService<DisposableProbe>();
        Assert.False(probe.IsDisposed);

        host.Dispose();

        Assert.True(probe.IsDisposed);
    }

    [Fact]
    public async Task StopAsyncKeepsStoppingRemainingServicesWhenOneThrows()
    {
        var log = new List<string>();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(log);
                // Registered first so it stops last, after the healthy service.
                services.AddSingleton<IHostedService>(sp => new ThrowingHostedService("B", sp.GetRequiredService<List<string>>()));
                services.AddSingleton<IHostedService>(sp => new TrackingHostedService("A", sp.GetRequiredService<List<string>>()));
            })
            .Build();

        try
        {
            await host.StartAsync();

            var stopError = await Record.ExceptionAsync(() => host.StopAsync());
            Assert.NotNull(stopError);
        }
        finally
        {
            host.Dispose();
        }

        Assert.Contains("A:stop", log);
        Assert.Contains("B:stop", log);
    }

    private sealed class TrackingHostedService : IHostedService
    {
        private readonly string _name;
        private readonly List<string> _log;

        public TrackingHostedService(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _log.Add($"{_name}:start");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _log.Add($"{_name}:stop");
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingHostedService : IHostedService
    {
        private readonly string _name;
        private readonly List<string> _log;

        public ThrowingHostedService(string name, List<string> log)
        {
            _name = name;
            _log = log;
        }

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _log.Add($"{_name}:stop");
            return Task.FromException(new InvalidOperationException("Stop failure"));
        }
    }

    private sealed class DisposableProbe : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }
}

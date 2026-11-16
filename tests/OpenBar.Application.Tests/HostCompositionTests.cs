using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace OpenBar.Application.Tests;

public sealed class HostCompositionTests
{
    [Fact]
    public void HostBuildsAndResolvesRegisteredServices()
    {
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<TestService>();
            })
            .Build();

        Assert.NotNull(host.Services.GetRequiredService<TestService>());
    }

    private sealed class TestService
    {
    }
}

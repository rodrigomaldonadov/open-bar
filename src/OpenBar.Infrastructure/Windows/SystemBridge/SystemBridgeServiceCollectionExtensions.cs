using Microsoft.Extensions.DependencyInjection;
using OpenBar.SystemBridge;

namespace OpenBar.Infrastructure.Windows.SystemBridge;

/// <summary>Registers the Windows source and the shared System Bridge service.</summary>
public static class SystemBridgeServiceCollectionExtensions
{
    public static IServiceCollection AddOpenBarSystemBridge(
        this IServiceCollection services,
        Action<SystemMetricsOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new SystemMetricsOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);
        services.AddSingleton<ISystemMetricsSource, WindowsSystemMetricsSource>();
        services.AddSingleton<SystemMetricsService>();
        services.AddSingleton<ISystemMetrics>(provider => provider.GetRequiredService<SystemMetricsService>());

        return services;
    }
}

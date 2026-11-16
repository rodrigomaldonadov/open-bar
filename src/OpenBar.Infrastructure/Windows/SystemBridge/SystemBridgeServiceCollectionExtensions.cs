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

        services.AddSingleton<IBatteryMetricsSource, WindowsBatteryMetricsSource>();
        services.AddSingleton<BatteryMetricsService>();
        services.AddSingleton<IBatteryMetrics>(provider => provider.GetRequiredService<BatteryMetricsService>());

        services.AddSingleton<INetworkMetricsSource, WindowsNetworkMetricsSource>();
        services.AddSingleton<NetworkMetricsService>();
        services.AddSingleton<INetworkMetrics>(provider => provider.GetRequiredService<NetworkMetricsService>());

        services.AddSingleton<IStorageMetricsSource, WindowsStorageMetricsSource>();
        services.AddSingleton<StorageMetricsService>();
        services.AddSingleton<IStorageMetrics>(provider => provider.GetRequiredService<StorageMetricsService>());

        return services;
    }
}

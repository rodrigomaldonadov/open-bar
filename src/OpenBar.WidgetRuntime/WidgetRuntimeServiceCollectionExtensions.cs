using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenBar.SystemBridge;

namespace OpenBar.WidgetRuntime;

/// <summary>
/// Registers the runtime engine. The host must already provide an
/// <c>IWidgetWindowService</c> (App) and the persistence services; official
/// widget definitions are registered as <c>WidgetDefinition</c> singletons
/// before this call.
/// </summary>
public static class WidgetRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddOpenBarWidgetRuntime(
        this IServiceCollection services,
        Action<WidgetSchedulerOptions>? configureScheduler = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var schedulerOptions = new WidgetSchedulerOptions();
        configureScheduler?.Invoke(schedulerOptions);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISystemMetrics>(NullSystemMetrics.Instance);
        services.TryAddSingleton<IBatteryMetrics>(NullBatteryMetrics.Instance);
        services.TryAddSingleton<INetworkMetrics>(NullNetworkMetrics.Instance);
        services.TryAddSingleton<IStorageMetrics>(NullStorageMetrics.Instance);
        services.TryAddSingleton<OpenBar.Application.Widgets.IWidgetSurfaceService>(
            OpenBar.Application.Widgets.NullWidgetSurfaceService.Instance);
        services.AddSingleton(schedulerOptions);
        services.AddSingleton<WidgetScheduler>();
        services.AddSingleton<IWidgetRegistry, WidgetRegistry>();
        services.AddSingleton<IWidgetDefinitionProvider, ConfiguredWidgetDefinitionProvider>();
        services.AddSingleton<WidgetDiscoveryService>();
        services.AddSingleton<WidgetRuntimeService>();
        services.AddSingleton<IWidgetRuntime>(provider => provider.GetRequiredService<WidgetRuntimeService>());
        services.AddSingleton<WidgetRuntimeHostedService>();
        services.AddHostedService(provider => provider.GetRequiredService<WidgetRuntimeHostedService>());

        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using OpenBar.Application.Dock;

namespace OpenBar.Dock;

/// <summary>
/// Registers the platform-neutral dock engine. Windows adapters are registered
/// by <c>AddOpenBarDockInfrastructure</c>; the WinUI window lives in the App.
/// </summary>
public static class DockServiceCollectionExtensions
{
    public static IServiceCollection AddOpenBarDock(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDockController, DockController>();
        services.AddTransient<DockAutoHideController>();

        return services;
    }
}

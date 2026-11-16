using Microsoft.Extensions.DependencyInjection;
using OpenBar.Application.Dock;

namespace OpenBar.Infrastructure.Windows.Dock;

/// <summary>Registers the Windows-specific dock adapters.</summary>
public static class DockInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddOpenBarDockInfrastructure(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IApplicationCatalogService, StartMenuApplicationCatalogService>();
        services.AddSingleton<IShortcutResolver, ShortcutResolver>();
        services.AddSingleton<IApplicationLauncher, ApplicationLauncher>();
        services.AddSingleton<IAppWindowService, AppWindowService>();

        return services;
    }
}

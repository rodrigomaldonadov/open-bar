using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenBar.Application.Persistence;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// Registers the persistence stack. The hosted initialization service is added
/// last so callers can register it before other hosted services that expect the
/// database to be ready.
/// </summary>
public static class PersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddOpenBarPersistence(
        this IServiceCollection services,
        OpenBarDatabaseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var effectiveOptions = options ?? OpenBarDatabaseOptions.CreateDefault();
        effectiveOptions.Validate();

        services.AddSingleton(effectiveOptions);
        services.AddSingleton<SqliteConnectionInterceptor>();
        services.AddSingleton<OpenBarDbContextFactory>();
        services.AddSingleton<IDbContextFactory<OpenBarDbContext>>(
            provider => provider.GetRequiredService<OpenBarDbContextFactory>());

        services.AddSingleton<IDatabaseInitializer, SqliteDatabaseInitializer>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IWidgetInstanceService, WidgetInstanceService>();
        services.AddSingleton<IDockLayoutService, DockLayoutService>();
        services.AddSingleton<IPinnedApplicationService, PinnedApplicationService>();
        services.AddSingleton<PersistedConfigurationCache>();

        services.AddSingleton<PersistenceStartupService>();
        services.AddHostedService(provider => provider.GetRequiredService<PersistenceStartupService>());

        return services;
    }
}

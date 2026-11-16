using Microsoft.EntityFrameworkCore;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// Creates short-lived <see cref="OpenBarDbContext"/> instances. Registered as
/// a singleton factory; contexts themselves are never shared across
/// concurrent operations.
/// </summary>
public sealed class OpenBarDbContextFactory : IDbContextFactory<OpenBarDbContext>
{
    private readonly DbContextOptions<OpenBarDbContext> _options;

    public OpenBarDbContextFactory(
        OpenBarDatabaseOptions options,
        SqliteConnectionInterceptor interceptor)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(interceptor);

        options.Validate();

        _options = new DbContextOptionsBuilder<OpenBarDbContext>()
            .UseSqlite(SqliteConnectionStringFactory.Create(options))
            .AddInterceptors(interceptor)
            .Options;
    }

    public OpenBarDbContext CreateDbContext() => new(_options);
}

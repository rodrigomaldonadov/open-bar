using Microsoft.EntityFrameworkCore.Design;
using OpenBar.Infrastructure.Persistence;

namespace OpenBar.DesignTime;

/// <summary>
/// Design-time factory used only by <c>dotnet ef</c>. The model is built from
/// the real default configuration; generating migrations does not open or
/// modify the user database file.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<OpenBarDbContext>
{
    public OpenBarDbContext CreateDbContext(string[] args)
    {
        var options = OpenBarDatabaseOptions.CreateDefault();
        var interceptor = new SqliteConnectionInterceptor(options);
        return new OpenBarDbContextFactory(options, interceptor).CreateDbContext();
    }
}

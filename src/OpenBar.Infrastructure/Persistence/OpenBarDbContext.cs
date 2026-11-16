using Microsoft.EntityFrameworkCore;
using OpenBar.Domain.Dock;
using OpenBar.Domain.Layouts;
using OpenBar.Domain.Settings;
using OpenBar.Domain.Widgets;

namespace OpenBar.Infrastructure.Persistence;

/// <summary>
/// EF Core context for OpenBar. A context must never be shared between
/// concurrent operations; resolve <see cref="IDbContextFactory{OpenBarDbContext}"/>
/// and create a short-lived context per unit of work.
/// </summary>
public sealed class OpenBarDbContext : DbContext
{
    public OpenBarDbContext(DbContextOptions<OpenBarDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<WidgetInstance> WidgetInstances => Set<WidgetInstance>();

    public DbSet<DockLayout> DockLayouts => Set<DockLayout>();

    public DbSet<PinnedApplication> PinnedApplications => Set<PinnedApplication>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OpenBarDbContext).Assembly);
    }
}

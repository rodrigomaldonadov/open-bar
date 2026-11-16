using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenBar.Domain.Dock;

namespace OpenBar.Infrastructure.Persistence.Configurations;

internal sealed class PinnedApplicationConfiguration : IEntityTypeConfiguration<PinnedApplication>
{
    public void Configure(EntityTypeBuilder<PinnedApplication> builder)
    {
        builder.ToTable(
            "PinnedApplications",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_PinnedApplications_Name_Length",
                    "length(\"Name\") > 0 AND length(\"Name\") <= 200");
                table.HasCheckConstraint(
                    "CK_PinnedApplications_Target_Length",
                    "length(\"Target\") > 0 AND length(\"Target\") <= 2048");
                table.HasCheckConstraint("CK_PinnedApplications_SortOrder", "\"SortOrder\" >= 0");
            });

        builder.HasKey(application => application.Id);

        builder.Property(application => application.Name)
            .HasMaxLength(PinnedApplication.MaxNameLength)
            .IsRequired();

        builder.Property(application => application.Target)
            .HasMaxLength(PinnedApplication.MaxTargetLength)
            .IsRequired();

        builder.Property(application => application.Arguments)
            .HasMaxLength(PinnedApplication.MaxArgumentsLength);

        builder.Property(application => application.TargetKind)
            .HasConversion<string>()
            .HasMaxLength(24)
            .IsRequired();

        builder.Property(application => application.ResolvedTarget)
            .HasMaxLength(PinnedApplication.MaxResolvedTargetLength);

        builder.Property(application => application.AppUserModelId)
            .HasMaxLength(PinnedApplication.MaxAppUserModelIdLength);

        builder.Property(application => application.IconKey)
            .HasMaxLength(PinnedApplication.MaxIconKeyLength);

        builder.Property(application => application.SortOrder)
            .IsRequired();

        builder.Property(application => application.CreatedAt)
            .IsRequired();

        builder.Property(application => application.UpdatedAt)
            .IsRequired();

        builder.HasIndex(application => application.Target)
            .IsUnique()
            .HasDatabaseName("UX_PinnedApplications_Target");

        builder.HasIndex(application => application.SortOrder)
            .HasDatabaseName("IX_PinnedApplications_SortOrder");
    }
}

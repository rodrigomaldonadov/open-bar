using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenBar.Domain.Layouts;

namespace OpenBar.Infrastructure.Persistence.Configurations;

internal sealed class DockLayoutConfiguration : IEntityTypeConfiguration<DockLayout>
{
    public void Configure(EntityTypeBuilder<DockLayout> builder)
    {
        builder.ToTable(
            "DockLayouts",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_DockLayouts_Name_Length",
                    "length(\"Name\") > 0 AND length(\"Name\") <= 200");
                table.HasCheckConstraint(
                    "CK_DockLayouts_Size",
                    "\"Size\" > 0");
                table.HasCheckConstraint(
                    "CK_DockLayouts_Offset",
                    "\"Offset\" >= 0");
                table.HasCheckConstraint(
                    "CK_DockLayouts_IconSize",
                    "\"IconSize\" > 0 AND \"IconSize\" <= 256");
                table.HasCheckConstraint(
                    "CK_DockLayouts_Spacing",
                    "\"Spacing\" >= 0 AND \"Spacing\" <= 64");
                table.HasCheckConstraint(
                    "CK_DockLayouts_Magnification",
                    "\"Magnification\" >= 1 AND \"Magnification\" <= 2");
                table.HasCheckConstraint(
                    "CK_DockLayouts_MaterialOpacity",
                    "\"MaterialOpacity\" >= 0 AND \"MaterialOpacity\" <= 1");
                table.HasCheckConstraint(
                    "CK_DockLayouts_AnimationDuration",
                    "\"AnimationDurationMs\" >= 0 AND \"AnimationDurationMs\" <= 2000");
                table.HasCheckConstraint(
                    "CK_DockLayouts_MonitorTarget_Length",
                    "length(\"MonitorTarget\") > 0 AND length(\"MonitorTarget\") <= 100");
            });

        builder.HasKey(layout => layout.Id);

        builder.Property(layout => layout.Name)
            .HasMaxLength(DockLayout.MaxNameLength)
            .IsRequired();

        builder.Property(layout => layout.Position)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(layout => layout.Alignment)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(layout => layout.Size)
            .IsRequired();

        builder.Property(layout => layout.Offset)
            .IsRequired();

        builder.Property(layout => layout.IconSize)
            .IsRequired();

        builder.Property(layout => layout.Spacing)
            .IsRequired();

        builder.Property(layout => layout.Magnification)
            .IsRequired();

        builder.Property(layout => layout.MaterialOpacity)
            .IsRequired();

        builder.Property(layout => layout.AnimationDurationMs)
            .IsRequired();

        builder.Property(layout => layout.AutoHide)
            .IsRequired();

        builder.Property(layout => layout.IsDefault)
            .IsRequired();

        builder.Property(layout => layout.MonitorTarget)
            .HasMaxLength(DockLayout.MaxMonitorTargetLength)
            .IsRequired();

        builder.Property(layout => layout.CreatedAt)
            .IsRequired();

        builder.Property(layout => layout.UpdatedAt)
            .IsRequired();

        builder.HasIndex(layout => layout.Name)
            .IsUnique()
            .HasDatabaseName("UX_DockLayouts_Name");

        builder.HasIndex(layout => layout.IsDefault)
            .IsUnique()
            .HasFilter("\"IsDefault\" = 1")
            .HasDatabaseName("UX_DockLayouts_IsDefault");
    }
}

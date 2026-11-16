using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenBar.Domain.Widgets;

namespace OpenBar.Infrastructure.Persistence.Configurations;

internal sealed class WidgetInstanceConfiguration : IEntityTypeConfiguration<WidgetInstance>
{
    public void Configure(EntityTypeBuilder<WidgetInstance> builder)
    {
        builder.ToTable(
            "WidgetInstances",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_WidgetInstances_WidgetId_Length",
                    "length(\"WidgetId\") > 0 AND length(\"WidgetId\") <= 100");
                table.HasCheckConstraint(
                    "CK_WidgetInstances_InstanceName_Length",
                    "length(\"InstanceName\") > 0 AND length(\"InstanceName\") <= 200");
                table.HasCheckConstraint(
                    "CK_WidgetInstances_SettingsSchemaVersion",
                    "\"SettingsSchemaVersion\" >= 1");
                table.HasCheckConstraint(
                    "CK_WidgetInstances_SettingsJson_Valid",
                    "json_valid(\"SettingsJson\")");
                table.HasCheckConstraint(
                    "CK_WidgetInstances_Width",
                    "\"Width\" IS NULL OR \"Width\" > 0");
                table.HasCheckConstraint(
                    "CK_WidgetInstances_Height",
                    "\"Height\" IS NULL OR \"Height\" > 0");
                table.HasCheckConstraint(
                    "CK_WidgetInstances_PresentationOrder",
                    "\"PresentationOrder\" >= 0");
            });

        builder.HasKey(instance => instance.Id);

        builder.Property(instance => instance.WidgetId)
            .HasMaxLength(WidgetInstance.MaxWidgetIdLength)
            .IsRequired();

        builder.Property(instance => instance.InstanceName)
            .HasMaxLength(WidgetInstance.MaxInstanceNameLength)
            .IsRequired();

        builder.Property(instance => instance.IsEnabled)
            .IsRequired();

        builder.Property(instance => instance.PresentationMode)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(instance => instance.PresentationOrder)
            .IsRequired();

        builder.Property(instance => instance.SettingsJson)
            .IsRequired();

        builder.Property(instance => instance.SettingsSchemaVersion)
            .IsRequired();

        builder.Property(instance => instance.CreatedAt)
            .IsRequired();

        builder.Property(instance => instance.UpdatedAt)
            .IsRequired();

        builder.HasIndex(instance => new { instance.WidgetId, instance.InstanceName })
            .IsUnique()
            .HasDatabaseName("UX_WidgetInstances_WidgetId_InstanceName");

        builder.HasIndex(instance => instance.IsEnabled)
            .HasDatabaseName("IX_WidgetInstances_IsEnabled");
    }
}

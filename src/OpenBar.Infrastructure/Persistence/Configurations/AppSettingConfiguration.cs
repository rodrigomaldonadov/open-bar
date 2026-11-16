using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenBar.Domain.Settings;

namespace OpenBar.Infrastructure.Persistence.Configurations;

internal sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable(
            "AppSettings",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_AppSettings_Key_Length",
                    "length(\"Key\") > 0 AND length(\"Key\") <= 200");
                table.HasCheckConstraint(
                    "CK_AppSettings_Value_Length",
                    "length(\"Value\") <= 32768");
            });

        builder.HasKey(setting => setting.Key);

        builder.Property(setting => setting.Key)
            .HasMaxLength(AppSetting.MaxKeyLength)
            .IsRequired();

        builder.Property(setting => setting.Value)
            .IsRequired();

        builder.Property(setting => setting.CreatedAt)
            .IsRequired();

        builder.Property(setting => setting.UpdatedAt)
            .IsRequired();
    }
}

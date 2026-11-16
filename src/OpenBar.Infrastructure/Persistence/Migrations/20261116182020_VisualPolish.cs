using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenBar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VisualPolish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AutoHideHideDelayMs",
                table: "DockLayouts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 550);

            migrationBuilder.AddColumn<int>(
                name: "AutoHideShowDelayMs",
                table: "DockLayouts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 150);

            migrationBuilder.AddColumn<double>(
                name: "CornerRadius",
                table: "DockLayouts",
                type: "REAL",
                nullable: false,
                defaultValue: 22.0);

            migrationBuilder.AddColumn<bool>(
                name: "HideInFullscreen",
                table: "DockLayouts",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReduceAnimations",
                table: "DockLayouts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Theme",
                table: "DockLayouts",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "System");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_AutoHideHideDelay",
                table: "DockLayouts",
                sql: "\"AutoHideHideDelayMs\" >= 0 AND \"AutoHideHideDelayMs\" <= 5000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_AutoHideShowDelay",
                table: "DockLayouts",
                sql: "\"AutoHideShowDelayMs\" >= 0 AND \"AutoHideShowDelayMs\" <= 2000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_CornerRadius",
                table: "DockLayouts",
                sql: "\"CornerRadius\" >= 0 AND \"CornerRadius\" <= 48");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_Theme",
                table: "DockLayouts",
                sql: "\"Theme\" IN ('System', 'Light', 'Dark')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_AutoHideHideDelay",
                table: "DockLayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_AutoHideShowDelay",
                table: "DockLayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_CornerRadius",
                table: "DockLayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_Theme",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "AutoHideHideDelayMs",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "AutoHideShowDelayMs",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "CornerRadius",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "HideInFullscreen",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "ReduceAnimations",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "Theme",
                table: "DockLayouts");
        }
    }
}


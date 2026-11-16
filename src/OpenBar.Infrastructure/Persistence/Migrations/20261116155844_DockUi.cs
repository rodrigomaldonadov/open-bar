using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenBar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DockUi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AnimationDurationMs",
                table: "DockLayouts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 180);

            migrationBuilder.AddColumn<double>(
                name: "IconSize",
                table: "DockLayouts",
                type: "REAL",
                nullable: false,
                defaultValue: 48.0);

            migrationBuilder.AddColumn<double>(
                name: "Magnification",
                table: "DockLayouts",
                type: "REAL",
                nullable: false,
                defaultValue: 1.12);

            migrationBuilder.AddColumn<double>(
                name: "MaterialOpacity",
                table: "DockLayouts",
                type: "REAL",
                nullable: false,
                defaultValue: 0.55);

            migrationBuilder.AddColumn<double>(
                name: "Spacing",
                table: "DockLayouts",
                type: "REAL",
                nullable: false,
                defaultValue: 10.0);

            migrationBuilder.CreateTable(
                name: "PinnedApplications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Target = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Arguments = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    TargetKind = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    ResolvedTarget = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    AppUserModelId = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    IconKey = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PinnedApplications", x => x.Id);
                    table.CheckConstraint("CK_PinnedApplications_Name_Length", "length(\"Name\") > 0 AND length(\"Name\") <= 200");
                    table.CheckConstraint("CK_PinnedApplications_SortOrder", "\"SortOrder\" >= 0");
                    table.CheckConstraint("CK_PinnedApplications_Target_Length", "length(\"Target\") > 0 AND length(\"Target\") <= 2048");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_AnimationDuration",
                table: "DockLayouts",
                sql: "\"AnimationDurationMs\" >= 0 AND \"AnimationDurationMs\" <= 2000");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_IconSize",
                table: "DockLayouts",
                sql: "\"IconSize\" > 0 AND \"IconSize\" <= 256");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_Magnification",
                table: "DockLayouts",
                sql: "\"Magnification\" >= 1 AND \"Magnification\" <= 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_MaterialOpacity",
                table: "DockLayouts",
                sql: "\"MaterialOpacity\" >= 0 AND \"MaterialOpacity\" <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DockLayouts_Spacing",
                table: "DockLayouts",
                sql: "\"Spacing\" >= 0 AND \"Spacing\" <= 64");

            migrationBuilder.CreateIndex(
                name: "IX_PinnedApplications_SortOrder",
                table: "PinnedApplications",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "UX_PinnedApplications_Target",
                table: "PinnedApplications",
                column: "Target",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PinnedApplications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_AnimationDuration",
                table: "DockLayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_IconSize",
                table: "DockLayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_Magnification",
                table: "DockLayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_MaterialOpacity",
                table: "DockLayouts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DockLayouts_Spacing",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "AnimationDurationMs",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "IconSize",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "Magnification",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "MaterialOpacity",
                table: "DockLayouts");

            migrationBuilder.DropColumn(
                name: "Spacing",
                table: "DockLayouts");
        }
    }
}

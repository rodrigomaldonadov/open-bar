using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenBar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        private static readonly string[] WidgetInstanceColumns = ["WidgetId", "InstanceName"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                    table.CheckConstraint("CK_AppSettings_Key_Length", "length(\"Key\") > 0 AND length(\"Key\") <= 200");
                    table.CheckConstraint("CK_AppSettings_Value_Length", "length(\"Value\") <= 32768");
                });

            migrationBuilder.CreateTable(
                name: "DockLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Position = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Alignment = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Size = table.Column<double>(type: "REAL", nullable: false),
                    Offset = table.Column<double>(type: "REAL", nullable: false),
                    AutoHide = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    MonitorTarget = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockLayouts", x => x.Id);
                    table.CheckConstraint("CK_DockLayouts_MonitorTarget_Length", "length(\"MonitorTarget\") > 0 AND length(\"MonitorTarget\") <= 100");
                    table.CheckConstraint("CK_DockLayouts_Name_Length", "length(\"Name\") > 0 AND length(\"Name\") <= 200");
                    table.CheckConstraint("CK_DockLayouts_Offset", "\"Offset\" >= 0");
                    table.CheckConstraint("CK_DockLayouts_Size", "\"Size\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "WidgetInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    WidgetId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    InstanceName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    PositionX = table.Column<double>(type: "REAL", nullable: true),
                    PositionY = table.Column<double>(type: "REAL", nullable: true),
                    Width = table.Column<double>(type: "REAL", nullable: true),
                    Height = table.Column<double>(type: "REAL", nullable: true),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    SettingsSchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetInstances", x => x.Id);
                    table.CheckConstraint("CK_WidgetInstances_Height", "\"Height\" IS NULL OR \"Height\" > 0");
                    table.CheckConstraint("CK_WidgetInstances_InstanceName_Length", "length(\"InstanceName\") > 0 AND length(\"InstanceName\") <= 200");
                    table.CheckConstraint("CK_WidgetInstances_SettingsJson_Valid", "json_valid(\"SettingsJson\")");
                    table.CheckConstraint("CK_WidgetInstances_SettingsSchemaVersion", "\"SettingsSchemaVersion\" >= 1");
                    table.CheckConstraint("CK_WidgetInstances_WidgetId_Length", "length(\"WidgetId\") > 0 AND length(\"WidgetId\") <= 100");
                    table.CheckConstraint("CK_WidgetInstances_Width", "\"Width\" IS NULL OR \"Width\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "UX_DockLayouts_IsDefault",
                table: "DockLayouts",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "UX_DockLayouts_Name",
                table: "DockLayouts",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WidgetInstances_IsEnabled",
                table: "WidgetInstances",
                column: "IsEnabled");

            migrationBuilder.CreateIndex(
                name: "UX_WidgetInstances_WidgetId_InstanceName",
                table: "WidgetInstances",
                columns: WidgetInstanceColumns,
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "DockLayouts");

            migrationBuilder.DropTable(
                name: "WidgetInstances");
        }
    }
}

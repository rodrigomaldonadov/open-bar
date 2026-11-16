using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenBar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IntegratedWidgetSurfaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PresentationMode",
                table: "WidgetInstances",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "FloatingWindow");

            migrationBuilder.AddColumn<int>(
                name: "PresentationOrder",
                table: "WidgetInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WidgetInstances_PresentationOrder",
                table: "WidgetInstances",
                sql: "\"PresentationOrder\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WidgetInstances_PresentationOrder",
                table: "WidgetInstances");

            migrationBuilder.DropColumn(
                name: "PresentationMode",
                table: "WidgetInstances");

            migrationBuilder.DropColumn(
                name: "PresentationOrder",
                table: "WidgetInstances");
        }
    }
}

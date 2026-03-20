using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class connectiontablerename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "environment",
                table: "Connections",
                newName: "Environment");

            migrationBuilder.RenameColumn(
                name: "service_owner",
                table: "Connections",
                newName: "ServiceOwner");

            migrationBuilder.AddColumn<string>(
                name: "SchemaVersion",
                table: "Connections",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "Connections");

            migrationBuilder.RenameColumn(
                name: "Environment",
                table: "Connections",
                newName: "environment");

            migrationBuilder.RenameColumn(
                name: "ServiceOwner",
                table: "Connections",
                newName: "service_owner");
        }
    }
}

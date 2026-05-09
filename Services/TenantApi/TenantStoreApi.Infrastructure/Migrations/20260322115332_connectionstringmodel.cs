using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantStoreApi.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class connectionstringmodel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ConnetionString",
                table: "Connections",
                newName: "DatabaseName");

            migrationBuilder.AddColumn<string>(
                name: "ClusterId",
                table: "Connections",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ConnectionString",
                table: "Connections",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClusterId",
                table: "Connections");

            migrationBuilder.DropColumn(
                name: "ConnectionString",
                table: "Connections");

            migrationBuilder.RenameColumn(
                name: "DatabaseName",
                table: "Connections",
                newName: "ConnetionString");
        }
    }
}
